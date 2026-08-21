using System.Collections.Concurrent;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed class HierarchyModelStore : IHierarchyModelStore, IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<ShortGuid, Entry> _entries = new();

    public HierarchyModelStore(TimeSpan? idleTimeout = null)
    {
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
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

    public void Remove(ShortGuid watchId)
    {
        if (_entries.TryRemove(watchId, out var entry))
        {
            entry.Watcher?.Dispose();
            entry.IdleTimer?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var watchId in _entries.Keys.ToList())
        {
            Remove(watchId);
        }
    }

    private Entry CreateEntry(ShortGuid watchId, string rootPath)
    {
        var entry = new Entry { Model = new HierarchyModel(rootPath) };
        entry.IdleTimer = new Timer(_ => EvictIfIdle(watchId), null, _idleTimeout, Timeout.InfiniteTimeSpan);
        return entry;
    }

    private void EvictIfIdle(ShortGuid watchId)
    {
        if (_entries.TryGetValue(watchId, out var entry) && entry.Watcher is null)
        {
            Remove(watchId);
        }
    }

    private sealed class Entry
    {
        public required HierarchyModel Model { get; init; }
        public RootFolderWatcher? Watcher { get; set; }
        public Timer? IdleTimer { get; set; }
    }
}
