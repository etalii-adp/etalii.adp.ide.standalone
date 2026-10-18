using Serilog;

namespace EtAlii.Adp.Hierarchy;

public sealed class HierarchyModelStore : IHierarchyModelStore, IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly ILogger _logger = Log.ForContext<HierarchyModelStore>();

    // A model built by ListEntries that no WatchHierarchy ever claims - a client that listed
    // and then went away - is dropped once the idle window passes; the watch's claim keeps
    // it. The registry logs each such drop, worth seeing because a steady stream of them means
    // clients are not opening the watch they were expected to.
    private readonly RetainedRegistry<ShortGuid, HierarchyModelEntry> _entries;

    private readonly ToolRouter _tools;

    public HierarchyModelStore(ToolRouter tools, TimeSpan? idleTimeout = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tools);

        _tools = tools;
        _entries = new RetainedRegistry<ShortGuid, HierarchyModelEntry>(
            idleTimeout ?? DefaultIdleTimeout,
            (_, _) => { },
            time ?? TimeProvider.System,
            dropUnclaimed: true);
    }

    public HierarchyModel GetOrCreate(ShortGuid watchId, ProjectRoot root) =>
        _entries.GetOrAdd(watchId, _ => Create(root)).Model;

    public HierarchyModelClaim Claim(ShortGuid watchId, ProjectRoot root)
    {
        var entry = _entries.Retain(watchId, _ => Create(root));
        return new HierarchyModelClaim(entry.Model, () => Remove(watchId));
    }

    public void NotifyRenamed(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(newPath);

        // Every model that contains the moved entry - a project may be watched on several
        // connections at once, each with its own private view, so each is told directly rather
        // than one relying on another connection's watcher. A model whose root does not contain
        // the entry simply skips it.
        ForEachModelContaining(oldPath, model => model.ApplyLocalRename(oldPath, newPath));
    }

    public void ExpectRename(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(newPath);
        ForEachModelContaining(oldPath, model => model.ExpectLocalRename(oldPath, newPath));
    }

    public void WithdrawRename(string oldPath, string newPath)
    {
        ArgumentNullException.ThrowIfNull(newPath);
        ForEachModelContaining(oldPath, model => model.WithdrawLocalRename(oldPath, newPath));
    }

    private void ForEachModelContaining(string path, Action<HierarchyModel> action)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        foreach (var entry in _entries.Values())
        {
            if (Contains(entry.Model.RootPath, fullPath))
            {
                action(entry.Model);
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> is the folder <paramref name="root"/> itself or something inside it.</summary>
    private static bool Contains(string root, string path) => PathContainment.IsAtOrInside(root, path);

    private void Remove(ShortGuid watchId)
    {
        if (_entries.Remove(watchId))
        {
            _logger.Debug("Dropped the hierarchy model for watch {WatchId}; {Remaining} still held", watchId, _entries.Count);
        }
    }

    public void Dispose() => _entries.Dispose();

    private HierarchyModelEntry Create(ProjectRoot root) => new() { Model = new HierarchyModel(root.FullPath, _tools) };
}
