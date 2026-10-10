namespace EtAlii.Adp.Documents;

/// <summary>
/// Follows a rename made in the project explorer: what a tool type keeps in other files about
/// the renamed entry - a relation that names it by its path, say - is brought along.
/// </summary>
/// <remarks>
/// Called by the rename's own command after the entry has moved, and again when that rename is
/// undone or redone, which is one more rename the other way. So a follower holds no state of its
/// own: what it rewrote going one way it rewrites back going the other, and the rename with all
/// that followed it stays one undoable step.
/// </remarks>
public interface IEntryRenameFollower
{
    /// <param name="rootPath">The project the entry is in: how far a follower looks.</param>
    /// <param name="fromPath">Where the file or folder was.</param>
    /// <param name="toPath">Where it is now.</param>
    void Renamed(string rootPath, string fromPath, string toPath);
}
