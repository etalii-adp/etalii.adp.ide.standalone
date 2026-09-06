using System.Collections.Concurrent;
using EtAlii.Adp.Common;
using Serilog;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed class HierarchyModelStore : IHierarchyModelStore, IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly ILogger _logger = Log.ForContext<HierarchyModelStore>();

    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<ShortGuid, HierarchyModelEntry> _entries = new();

    private readonly IDiagramDefinitionCatalog? _catalog;
    private readonly DiagramFileRouter? _router;
    private readonly EditorResolver? _editorResolver;

    public HierarchyModelStore(IDiagramDefinitionCatalog? catalog = null, TimeSpan? idleTimeout = null, DiagramFileRouter? router = null, EditorResolver? editorResolver = null)
    {
        _catalog = catalog;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _router = router;
        _editorResolver = editorResolver;
    }

    public HierarchyModel GetOrCreate(ShortGuid watchId, string rootPath) =>
        _entries.GetOrAdd(watchId, _ => CreateEntry(watchId, rootPath)).Model;

    public void AttachWatcher(ShortGuid watchId, RootFolderWatcher watcher)
    {
        if (!_entries.TryGetValue(watchId, out var entry))
        {
            return;
        }

        entry.IdleTimer?.Dispose();
        entry.IdleTimer = null;
        entry.Watcher = watcher;
    }

    public void NotifyRenamed(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(oldPath);
        ArgumentNullException.ThrowIfNull(newPath);
        var old = System.IO.Path.GetFullPath(oldPath);

        // Every model that contains the moved entry - a project may be watched on several
        // connections at once, each with its own private view, so each is told directly rather
        // than one relying on another connection's watcher. A model whose root does not contain
        // the entry simply skips it.
        foreach (var entry in _entries.Values)
        {
            if (Contains(entry.Model.RootPath, old))
            {
                entry.Model.ApplyLocalRename(oldPath, newPath);
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> is the folder <paramref name="root"/> itself or something inside it.</summary>
    private static bool Contains(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root.EndsWith(System.IO.Path.DirectorySeparatorChar) ? root : root + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public void Remove(ShortGuid watchId)
    {
        if (_entries.TryRemove(watchId, out var entry))
        {
            entry.Watcher?.Dispose();
            entry.IdleTimer?.Dispose();
            _logger.Debug("Dropped the hierarchy model for watch {WatchId}; {Remaining} still held", watchId, _entries.Count);
        }
    }

    public void Dispose()
    {
        foreach (var watchId in _entries.Keys.ToList())
        {
            Remove(watchId);
        }
    }

    private HierarchyModelEntry CreateEntry(ShortGuid watchId, string rootPath)
    {
        var entry = new HierarchyModelEntry
        {
            Model = new HierarchyModel(rootPath, _catalog, _router, _editorResolver),
            IdleTimer = new Timer(_ => EvictIfIdle(watchId), null, _idleTimeout, Timeout.InfiniteTimeSpan),
        };
        return entry;
    }

    private void EvictIfIdle(ShortGuid watchId)
    {
        if (_entries.TryGetValue(watchId, out var entry) && entry.Watcher is null)
        {
            // A model built by ListEntries that no WatchHierarchy ever claimed - a client that
            // listed and then went away. Worth seeing, because a steady stream of these means
            // clients are not opening the watch they were expected to.
            _logger.Debug(
                "Evicting the hierarchy model for watch {WatchId}: no watch attached within {IdleTimeout}",
                watchId,
                _idleTimeout);
            Remove(watchId);
        }
    }

}
