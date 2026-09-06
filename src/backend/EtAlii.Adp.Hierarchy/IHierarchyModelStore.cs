namespace EtAlii.Adp.Hierarchy;

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

    /// <summary>
    /// Applies a rename a command just performed on disk to every connection's model that
    /// contains the moved entry - the deterministic, cross-platform source of a stable-id
    /// rename, so the tree does not depend on the FileSystemWatcher agreeing about a move's
    /// shape (which Linux and Windows do not: Windows reports one Renamed, Linux an uncorrelated
    /// Delete and Create). The watcher's own later echo of the move is suppressed per model.
    /// Called for forward, undo and redo alike, because all three run through the rename handler.
    /// </summary>
    void NotifyRenamed(string oldPath, string newPath);
}
