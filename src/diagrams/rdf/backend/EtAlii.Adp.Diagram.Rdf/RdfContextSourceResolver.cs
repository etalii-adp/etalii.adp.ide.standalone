using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Makes an RDF element selectable: resolves an <c>element_id</c> against the file named by the
/// enclosing level and verifies the id really is in it - once for the whole family, because
/// which reading a file is opened under does not change what an element is.
/// </summary>
public sealed class RdfContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IRdfDocumentStore _documents;

    public RdfContextSourceResolver(DiagramFileRouter router, IRdfDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);

        _router = router;
        _documents = documents;
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

        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin.Vendor != "w3c" ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not an RDF document.");
        }

        var elementId = id.ElementId.Value;

        // A placement or a finished relation gesture names the element about to exist - it
        // resolves like any element, to a target the action provider reads the gesture back out
        // of; it lives for one ExecuteAction and is never selected, tracked or written anywhere.
        if (RdfNewPlacement.TryParse(elementId, out _, out _) ||
            RdfRelationGesture.TryParse(elementId, out _, out _))
        {
            return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
                source,
                id,
                ["New element"],
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
                new ContextLevelDetail { Element = new ElementDetail { Text = "New element" } },
                this)));
        }

        var entry = _documents.GetOrLoad(bodyPath);
        var text = RdfSelection.Describe(entry, elementId) ?? OwlSelection.Describe(entry, elementId);
        if (text is null)
        {
            return Rejected("That element is no longer in this file.");
        }

        // The client's path is checked, never trusted.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual([text], StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var level = new ContextResolvedLevel(
            source,
            id,
            [text],
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
            new ContextLevelDetail { Element = new ElementDetail { Text = text } },
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>Nothing nests inside these elements for selection purposes.</summary>
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

        void OnChanged(object? sender, RdfDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: a removal clears the selection.
            var text = RdfSelection.Describe(args.Entry, elementId) ?? OwlSelection.Describe(args.Entry, elementId);
            onChange(text is null ? null : [text]);
        }

        _documents.Changed += OnChanged;
        return new RdfUnsubscriber(() => _documents.Changed -= OnChanged);
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
