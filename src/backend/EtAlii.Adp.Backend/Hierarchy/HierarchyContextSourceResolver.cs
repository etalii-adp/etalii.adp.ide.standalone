using EtAlii.Adp.Backend.Context;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

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

    /// <param name="router">Says whether a file is a diagram, which is what decides that it may contain a selected element.</param>
    public HierarchyContextSourceResolver(IHierarchyModelStore hierarchyModelStore, DiagramFileRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _hierarchyModelStore = hierarchyModelStore;
        _router = router;
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
            Entry = new EntryDetail { Kind = isFolder ? EntryKind.Folder : EntryKind.File, Available = IsAvailable(fullPath, isFolder) },
        };

        var level = new ContextResolvedLevel(
            source,
            id,
            relativePath,
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, fullPath, isFolder, entryId, rootPath, watchId),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ContextLevelResolution.Resolved(level));
    }

    /// <summary>
    /// A folder contains entries. A file contains nothing of this kind - unless it is a
    /// diagram, which contains elements: a node selected on the canvas nests under its
    /// .adp file (mindmap-diagram Requirement 10.1), resolved by the diagram type's own
    /// resolver rather than this one.
    /// </summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) =>
        level.Target.IsContainer || _router.Route(level.Target.ResolvedFullPath) is DiagramRouting.Routed
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
            if (disposed || change is not (HierarchyEntryChange.Renamed or HierarchyEntryChange.Removed))
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
        return new Subscription(() =>
        {
            disposed = true;
            model.EntryChanged -= OnEntryChanged;
        });
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new ContextLevelResolution.Rejected(reason));

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

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        public Subscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
