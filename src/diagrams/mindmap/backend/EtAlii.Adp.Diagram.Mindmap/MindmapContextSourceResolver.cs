using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Makes a mindmap node selectable: resolves an <c>element_id</c> against the map named by
/// the enclosing selection level, verifies the node really is in that map, and fills in the
/// detail a consumer shows (Requirements 10.2, 10.4, 10.6), unfolding the branches that hide
/// it (Requirement 10.5). Registering this is the whole of
/// it; the context service itself is untouched.
/// </summary>
public sealed class MindmapContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;

    public MindmapContextSourceResolver(DiagramFileRouter router, IMindmapDocumentStore documents, MindmapViewState views)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(views);
        _router = router;
        _documents = documents;
        _views = views;
    }

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        // A node is only ever selected inside a diagram: with no file level above it there is
        // nothing to verify it against, and an unverifiable selection is never recorded.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath) is not DiagramRouted { Definition.Origin: var origin, BodyPath: var bodyPath } ||
            origin != Diagram.Mindmap.Origin)
        {
            return Rejected("The selected file is not a mindmap.");
        }

        MindmapDocument document;
        try
        {
            document = _documents.GetOrLoad(bodyPath!);
        }
        catch (MindmapFormatException)
        {
            return Rejected("The mindmap cannot be read.");
        }

        var nodeId = id.ElementId.Value;
        var node = document.Find(nodeId);
        if (node is null)
        {
            return Rejected("Unknown node.");
        }

        // The path is the node's chain of texts from the root, relative to the file level
        // (Requirement 10.1); the client's version is checked, never trusted.
        var relativePath = PathOf(node);
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(relativePath, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the node.");
        }

        // A selection expands its collapsed ancestors so the node is actually visible
        // (Requirement 10.5; DISL's revealExpands, true by default). Each goes through the fold
        // toggle, outermost first, so the session holding this connection's stream pushes the
        // ungroup delta that brings the hidden node to the client.
        var view = _views.For(watchId, bodyPath!, document);
        foreach (var ancestor in view.FoldedAncestorsOf(node))
        {
            _views.Toggle(watchId, bodyPath!, document, ancestor.Id);
        }

        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = node.Text,
                HasChildren = node.HasChildren,
                Folded = view.IsFolded(node),
                Linked = node.Link is not null,
            },
        };

        var level = new ContextResolvedLevel(
            id,
            relativePath,
            ContextScope.DiagramElement,
            new ContextTarget(ContextScope.DiagramElement, bodyPath!, node.HasChildren, SourceId: default, rootPath, watchId, nodeId),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>Nothing nests inside a node for selection purposes; multi-level element chains are out of scope (Requirement 10.9).</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <summary>
    /// Re-resolves the node after every change to its map: a rename of an ancestor changes
    /// the path, a removal clears the selection (Requirement 10.7).
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        var bodyPath = level.Target.ResolvedFullPath;
        var nodeId = level.Target.ElementId;
        var lastPath = level.RelativePath;
        var disposed = false;

        void OnChanged(object? sender, MindmapChangedEventArgs args)
        {
            if (disposed || !string.Equals(args.BodyPath, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var node = _documents.Get(bodyPath)?.Find(nodeId);
            if (node is null)
            {
                onChange(null);
                return;
            }

            var currentPath = PathOf(node);
            if (!currentPath.SequenceEqual(lastPath, StringComparer.Ordinal))
            {
                lastPath = currentPath;
                onChange(currentPath);
            }
        }

        _documents.Changed += OnChanged;
        return new MindmapNodeSubscription(() =>
        {
            disposed = true;
            _documents.Changed -= OnChanged;
        });
    }

    private static string[] PathOf(MindmapNode node)
    {
        var segments = new List<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            segments.Insert(0, current.Text);
        }

        return [.. segments];
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

}
