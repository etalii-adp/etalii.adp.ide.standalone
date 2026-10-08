using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Resolves a node or a parent-to-child connection of a behavior model to a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent</b>: its payload comes from
/// <see cref="AbmElementMapper"/>, so the stream and the selection never describe one node two ways.
/// </para>
/// <para>
/// <b>A drop or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the node a
/// toolbox drop is about to add and <c>rel:from-&gt;to</c> the parent line a gesture proposes. Each
/// lives for one execution and is never selected, tracked or written.
/// </para>
/// </remarks>
public sealed class AbmContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IAbmDocumentStore _documents;
    private readonly AbmElementMapper _mapper;

    public AbmContextSourceResolver(DiagramFileRouter router, IAbmDocumentStore documents, AbmElementMapper mapper)
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

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Diagram.AgentBehaviorModelling.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a behavior model.");
        }

        var model = _documents.GetOrLoad(bodyPath).Model;
        var elementId = id.ElementId.Value;

        if (GestureIds.TryParsePlacement(elementId, out _, out _) || GestureIds.TryParseRelation(elementId, out _, out _))
        {
            var proposed = GestureIds.IsPlacement(elementId) ? "New node" : "New parent line";
            return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
                id,
                [proposed],
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
                new ContextLevelDetail { Element = new ElementDetail { Text = proposed } },
                this)));
        }

        var described = Describe(model, elementId);
        if (described is null)
        {
            return Rejected("That node is no longer in this behavior model.");
        }

        (IReadOnlyList<string> path, string text) = described.Value;

        // The client's path is checked, never trusted: it is what the client believes it selected,
        // and the id is what it actually selected.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(path, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = text,
                HasChildren = model.NodeOf(elementId) is { ChildIds.Count: > 0 },
                Folded = false,
                Linked = false,
            },
        };

        var drawn = _mapper.Visible(model, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal), DiagramViewport.Unbounded).FirstOrDefault(candidate => candidate.Id == elementId);
        if (drawn is not null)
        {
            detail.Element.ElementType = drawn.Type;
            detail.Element.Payload = new Any
            {
                TypeUrl = drawn.PayloadTypeUrl,
                Value = ByteString.CopyFrom(drawn.Payload.Span),
            };
        }

        var level = new ContextResolvedLevel(
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
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <inheritdoc />
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

        void OnChanged(object? sender, AbmDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: a rename keeps the id while changing
            // everything shown, and a removal clears the selection.
            onChange(Describe(_documents.GetOrLoad(bodyPath).Model, elementId)?.Path);
        }

        _documents.Changed += OnChanged;
        return new Unsubscriber(() => _documents.Changed -= OnChanged);
    }

    /// <summary>What a selection of <paramref name="id"/> shows, or null when nothing drawn has that id.</summary>
    private static (IReadOnlyList<string> Path, string Text)? Describe(AbmModel model, string id)
    {
        if (model.NodeOf(id) is { } node)
        {
            var text = node.Label.Length > 0 ? $"{node.Keyword}: {node.Label}" : node.Keyword;
            return ([text], text);
        }

        if (id.StartsWith(AbmElementMapper.ChildIdPrefix, StringComparison.Ordinal)
            && model.NodeOf(id[AbmElementMapper.ChildIdPrefix.Length..]) is { ParentId: { } parentId } child)
        {
            var parent = model.NodeOf(parentId)!;
            var text = $"{Named(parent)} → {Named(child)}";
            return ([text], text);
        }

        return null;
    }

    private static string Named(AbmNode node) => node.Label.Length > 0 ? node.Label : node.Keyword;

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
