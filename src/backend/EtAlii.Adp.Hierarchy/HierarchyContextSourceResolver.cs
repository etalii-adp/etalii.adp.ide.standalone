using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Resolves hierarchy entry ids for the context service, going only through the
/// connection's own <see cref="HierarchyModel"/> - so an id another connection handed
/// out resolves to nothing here, and containment is enforced in exactly one place.
/// Folders contain entries; a file contains nothing of this kind.
/// </summary>
public sealed class HierarchyContextSourceResolver : IContextSourceResolver
{
    private readonly IHierarchyModelStore _hierarchyModelStore;
    private readonly DiagramFileRouter _router;
    private readonly EditorResolver _editorResolver;

    /// <param name="hierarchyModelStore">The hierarchy model store.</param>
    /// <param name="router">Says whether a file is a diagram, which is what decides that it may contain a selected element.</param>
    /// <param name="editorResolver">
    /// Answers for the files the router does not claim, so an activation can open the resolved
    /// text editor's tab through the same mime-keyed mechanism a diagram uses
    /// (modular-text-editors Requirement 5.2).
    /// </param>
    public HierarchyContextSourceResolver(IHierarchyModelStore hierarchyModelStore, DiagramFileRouter router, EditorResolver editorResolver)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(editorResolver);
        _hierarchyModelStore = hierarchyModelStore;
        _router = router;
        _editorResolver = editorResolver;
    }

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.EntryId;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        var model = _hierarchyModelStore.GetOrCreate(watchId, rootPath);
        var entryId = (ShortGuid)id.EntryId;
        if (!model.TryResolvePath(entryId, out var fullPath, out var isFolder))
        {
            return Rejected("Unknown entry on this connection.");
        }

        var basePath = rootPath;
        if (parent is not null)
        {
            if (!parent.Target.IsContainer || !IsInside(parent.Target.ResolvedFullPath, fullPath))
            {
                return Rejected("The entry is not inside its parent.");
            }

            basePath = parent.Target.ResolvedFullPath;
        }

        var relativePath = RelativeSegments(basePath, fullPath);
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(relativePath, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the entry.");
        }

        var detail = new ContextLevelDetail
        {
            Entry = new EntryDetail
            {
                Kind = isFolder ? EntryKind.Folder : EntryKind.File,
                Available = IsAvailable(fullPath, isFolder),
                // The one word the workspace needs to open the right canvas: which diagram
                // this file is, said by the same routing that opens it - so the client never
                // grows a file-type table (diagram-workspace-tabs Requirement 1).
                DiagramMimeType = isFolder ? "" : DiagramMimeTypeOf(fullPath),
            },
        };

        var level = new ContextResolvedLevel(
            source,
            id,
            relativePath,
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, fullPath, isFolder, entryId, rootPath, watchId),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>
    /// A folder contains entries. A file contains nothing of this kind - unless it is a
    /// diagram, which contains elements: a node selected on the canvas nests under its
    /// .adp file (mindmap-diagram Requirement 10.1), resolved by the diagram type's own
    /// resolver rather than this one.
    /// </summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) =>
        level.Target.IsContainer || _router.Route(level.Target.ResolvedFullPath) is DiagramRouted
            ? ContextNesting.Contained
            : ContextNesting.NotNestable;

    /// <summary>
    /// Any rename or removal in the model may have moved the tracked entry - its own, or
    /// an ancestor folder's - so each one simply re-resolves the id and reports only a
    /// real difference. Re-resolving beats string-rewriting: the model already knows.
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        var model = _hierarchyModelStore.GetOrCreate(watchId, rootPath);
        var entryId = (ShortGuid)level.Id.EntryId;
        var basePath = BaseOf(level);
        var lastPath = level.RelativePath;
        var disposed = false;

        void OnEntryChanged(HierarchyEntryChange change)
        {
            if (disposed || change is not (HierarchyEntryRenamed or HierarchyEntryRemoved))
            {
                return;
            }

            if (!model.TryResolvePath(entryId, out var fullPath, out _))
            {
                onChange(null);
                return;
            }

            var currentPath = RelativeSegments(basePath, fullPath);
            if (!currentPath.SequenceEqual(lastPath, StringComparer.Ordinal))
            {
                lastPath = currentPath;
                onChange(currentPath);
            }
        }

        model.EntryChanged += OnEntryChanged;
        return new HierarchyEntrySubscription(() =>
        {
            disposed = true;
            model.EntryChanged -= OnEntryChanged;
        });
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private static bool IsInside(string parentFullPath, string fullPath)
    {
        var prefix = parentFullPath.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar) + IoPath.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] RelativeSegments(string basePath, string fullPath) =>
        IoPath.GetRelativePath(basePath, fullPath)
            .Split([IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The folder a level's relative path is relative to: its own location minus its own segments.</summary>
    private static string BaseOf(ContextResolvedLevel level)
    {
        var path = level.Target.ResolvedFullPath;
        for (var i = 0; i < level.RelativePath.Count; i++)
        {
            path = IoPath.GetDirectoryName(path) ?? path;
        }

        return path;
    }

    private static bool IsAvailable(string fullPath, bool isFolder) =>
        isFolder ? Directory.Exists(fullPath) : File.Exists(fullPath);

    /// <summary>
    /// The routed diagram type of a file, for the selection detail. <c>Routed</c> carries the
    /// definition's type and <c>UnknownType</c> the type the registration names - the client
    /// can then open a tab that says the type is unavailable rather than treating the file as
    /// plain. Everything else - no diagram, unreadable, or an extension more than one type
    /// claims - is empty: what the backend refuses to route is not presented as openable.
    /// </summary>
    private string DiagramMimeTypeOf(string fullPath) =>
        _router.Route(fullPath) switch
        {
            DiagramRouted routed => routed.Definition.Origin.MimeType,
            DiagramUnknownType unknown => unknown.MimeType,
            // Diagrams first, unconditionally - only what the router leaves falls through to
            // the editor family, whose tab keys on the same field ("editor/<id>"). An
            // ambiguous editor claim answers empty: the conflict was reported at startup, and
            // an activation that opens nothing beats one that picks an arbitrary rival.
            _ => _editorResolver.Resolve(fullPath) is EditorRouted editor ? $"editor/{editor.Definition.Id}" : "",
        };

}
