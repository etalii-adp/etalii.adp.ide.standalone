namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Owns the per-connection lifecycle of a <see cref="HierarchyModel"/> and its
/// <see cref="RootFolderWatcher"/> — strictly one of each per <c>watch_id</c>,
/// never shared, even across two connections to the same project.
/// </summary>
public interface IHierarchyModelStore
{
    HierarchyModel GetOrCreate(ShortGuid watchId, string rootPath);

    /// <summary>
    /// Attaches the watcher a `WatchHierarchy` call started for this <paramref name="watchId"/>,
    /// and cancels the idle-timeout eviction now that a stream has claimed the entry.
    /// </summary>
    void AttachWatcher(ShortGuid watchId, RootFolderWatcher watcher);

    /// <summary>Disposes and removes a connection's model and watcher (if attached).</summary>
    void Remove(ShortGuid watchId);
}
