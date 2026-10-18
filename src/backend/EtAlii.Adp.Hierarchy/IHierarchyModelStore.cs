namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Owns the per-connection lifecycle of a <see cref="HierarchyModel"/> — strictly one per
/// <c>watch_id</c>, never shared, even across two connections to the same project. Each watch's
/// subscription to the project's file events is held in its connection's scope; the watcher behind
/// the subscriptions is shared, one per project root (<see cref="Documents.ProjectFileEvents"/>).
/// </summary>
public interface IHierarchyModelStore
{
    HierarchyModel GetOrCreate(ShortGuid watchId, ProjectRoot root);

    /// <summary>
    /// Claims the model of this <paramref name="watchId"/> for a `WatchHierarchy` call, creating it
    /// when <c>ListEntries</c> has not, and cancels the idle-timeout eviction. The watch holds the
    /// claim in its connection's scope; releasing it removes the model, and is the only way to
    /// remove one (code-structure FR-067).
    /// </summary>
    HierarchyModelClaim Claim(ShortGuid watchId, ProjectRoot root);

    /// <summary>
    /// Applies a rename a command just performed on disk to every connection's model that
    /// contains the moved entry - the deterministic, cross-platform source of a stable-id
    /// rename, so the tree does not depend on the FileSystemWatcher agreeing about a move's
    /// shape (which Linux and Windows do not: Windows reports one Renamed, Linux an uncorrelated
    /// Delete and Create). The watcher's own later echo of the move is suppressed per model.
    /// Called for forward, undo and redo alike, because all three run through the rename handler.
    /// </summary>
    void NotifyRenamed(string oldPath, string newPath);

    /// <summary>
    /// Announces a rename a command is about to perform, before the move, to every model that
    /// contains the entry, so the watcher's echo is suppressed even when the watcher sees the
    /// move before <see cref="NotifyRenamed"/> is called. Followed by <see cref="NotifyRenamed"/>
    /// when the move happened, or by <see cref="WithdrawRename"/> when it did not.
    /// </summary>
    void ExpectRename(string oldPath, string newPath);

    /// <summary>Forgets a rename announced by <see cref="ExpectRename"/> that did not take place.</summary>
    void WithdrawRename(string oldPath, string newPath);
}
