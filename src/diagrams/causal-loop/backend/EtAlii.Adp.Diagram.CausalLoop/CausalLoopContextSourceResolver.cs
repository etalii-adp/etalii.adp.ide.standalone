using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Makes a variable, a link or a loop selectable: resolves an <c>element_id</c> against the
/// document named by the enclosing level, and verifies the id really is in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This module shipped without one, and nothing noticed.</b> A canvas selection is resolved
/// level by level - the project, the folders, the <c>.adp</c>, and then the element inside it -
/// and the last of those is each diagram type's own job. With no resolver the element level never
/// resolved, so no <see cref="ContextTarget"/> was ever built, so
/// <see cref="CausalLoopContextActionProvider"/> and
/// <see cref="CausalLoopContextPropertyProvider"/> were never consulted at all.
/// </para>
/// <para>
/// Every symptom followed from that one absence: no context menu, nothing ever showing as
/// selected, and an unreachable property grid. Sixteen provider tests passed throughout, because
/// they construct a target by hand and ask the provider directly - which is precisely the step
/// that was missing in the running application.
/// </para>
/// <para>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone whether
/// the element is one of its own - only the file above it says that. So this refuses a file that
/// is not a causal loop diagram and the selection resolver moves on to the next resolver, which
/// is what lets every module's element resolver coexist.
/// </para>
/// </remarks>
public sealed class CausalLoopContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly ICausalLoopDocumentStore _documents;
    private readonly CausalLoopElementMapper _mapper;

    /// <summary>Creates the resolver over the router and the one document store.</summary>
    public CausalLoopContextSourceResolver(
        DiagramFileRouter router,
        ICausalLoopDocumentStore documents,
        CausalLoopElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _router = router;
        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    /// <inheritdoc />
    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        // An element is only ever selected inside a diagram: with no file level above it there is
        // nothing to verify it against, and an unverifiable selection is never recorded.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        // The .adp names the body; the target carries the body, because that is what every
        // command, provider and document store in this module reads.
        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Diagram.CausalLoop.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a causal loop diagram.");
        }

        var elementId = id.ElementId.Value;

        // A placement names the variable about to exist at a point, and is how a right-click on
        // empty canvas carries where it landed through a channel that has one element id per call
        // and no other field. It is also what makes the diagram-wide actions reachable: with
        // nothing selected the diagram itself is the subject. It lives for one call and is never
        // selected, tracked or written anywhere.
        if (CausalLoopSelection.IsPlacement(elementId))
        {
            return Resolved(source, id, ["This diagram"], bodyPath, rootPath, watchId, elementId, routed, "This diagram", null, this);
        }

        // A relation gesture carries the two ends of a link the user drew, the same one-call,
        // one-id way a placement carries a point. It resolves to a transient target the connect
        // action reads the ends back out of; it names nothing and is never selected or stored.
        if (CausalLoopSelection.RelationOf(elementId) is not null)
        {
            return Resolved(source, id, ["New link"], bodyPath, rootPath, watchId, elementId, routed, "New link", null, this);
        }

        var model = _documents.GetOrLoad(bodyPath).Model;
        var text = Describe(model, elementId);
        if (text is null)
        {
            return Rejected("That element is no longer in this diagram.");
        }

        // The client's path is checked, never trusted: it is what the client believes it
        // selected, and the id is what it actually selected.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual([text], StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        // The same payload the canvas already has, taken from the mapper rather than assembled
        // here, so the selection and the stream can never describe one element two ways.
        var element = _mapper.Elements(model, CausalLoopLayout.Compute(model)).FirstOrDefault(candidate => candidate.Id == elementId);
        return Resolved(source, id, [text], bodyPath, rootPath, watchId, elementId, routed, text, element, this);
    }

    /// <summary>Nothing nests inside a variable, a link or a loop for selection purposes.</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <inheritdoc />
    public IDisposable Track(
        ShortGuid watchId,
        string rootPath,
        ContextResolvedLevel level,
        Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = watchId;
        _ = rootPath;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        void OnChanged(object? sender, CausalLoopDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: a rename keeps the id while
            // changing everything shown, and a removal clears the selection.
            var entry = _documents.GetOrLoad(bodyPath);
            var text = entry.IsUsable ? Describe(entry.Model, elementId) : null;
            onChange(text is null ? null : [text]);
        }

        _documents.Changed += OnChanged;
        return new CausalLoopSubscription(() => _documents.Changed -= OnChanged);
    }

    /// <summary>
    /// What an id reads as: a variable's label, a link named by its two ends, or a loop's caption.
    /// Null where the document no longer holds it.
    /// </summary>
    private static string? Describe(CausalLoopModel model, string elementId)
    {
        if (CausalLoopSelection.VariableOf(elementId) is { } variableId)
        {
            return model.Variables.FirstOrDefault(variable => variable.Id == variableId)?.Display;
        }

        if (CausalLoopSelection.LinkOf(elementId) is { } ends)
        {
            var link = model.Links.FirstOrDefault(candidate => candidate.From == ends.From && candidate.To == ends.To);
            return link is null ? null : $"{ends.From} → {ends.To}";
        }

        if (CausalLoopSelection.LoopOf(elementId) is { } identifier)
        {
            var loop = model.Loops.FirstOrDefault(candidate => candidate.Identifier == identifier);
            return loop is null ? null : loop.Name.Length > 0 ? $"{loop.Identifier} · {loop.Name}" : loop.Identifier;
        }

        return null;
    }

    private static ValueTask<ContextLevelResolution> Resolved(
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> path,
        string bodyPath,
        string rootPath,
        ShortGuid watchId,
        string elementId,
        DiagramRouted routed,
        string text,
        DiagramElement? element,
        IContextSourceResolver resolver)
    {
        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail { Text = text, HasChildren = false, Folded = false, Linked = false },
        };

        if (element is not null)
        {
            detail.Element.ElementType = element.Type;
            detail.Element.Payload = new Any
            {
                TypeUrl = element.PayloadTypeUrl,
                Value = ByteString.CopyFrom(element.Payload.Span),
            };
        }

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
            source,
            id,
            path,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                bodyPath,
                IsContainer: false,
                SourceId: default,
                rootPath,
                watchId,
                elementId,
                routed.Definition.Origin),
            detail,
            resolver)));
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    /// <summary>Undoes one <see cref="Track"/> subscription.</summary>
    private sealed class CausalLoopSubscription(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
