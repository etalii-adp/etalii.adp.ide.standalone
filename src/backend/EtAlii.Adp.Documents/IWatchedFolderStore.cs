namespace EtAlii.Adp.Documents;

/// <summary>
/// One snapshot per registered folder, kept true as the tree changes (code-structure FR-037).
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no save method, and no method that takes anything to write. A store for
/// a read-only diagram type has one job: hold what the folder says, and say when that changed.
/// </para>
/// <para>
/// Reading and lifetime are separate on purpose. <see cref="GetOrLoad"/> answers a question;
/// <see cref="Acquire"/> and <see cref="Release"/> are the pair a session holds across its own
/// lifetime. Without the split, a second connection closing would tear the watcher out from
/// under the first, and that connection's diagram would go quietly stale.
/// </para>
/// </remarks>
/// <typeparam name="TSnapshot">What one read of a folder makes; immutable, replaced wholesale.</typeparam>
public interface IWatchedFolderStore<out TSnapshot>
    where TSnapshot : class
{
    /// <summary>The snapshot of <paramref name="folder"/>, reading and watching it if this is the first ask.</summary>
    TSnapshot GetOrLoad(string folder);

    /// <summary>The snapshot of <paramref name="folder"/> if one is loaded, without loading one.</summary>
    TSnapshot? Get(string folder);

    /// <summary>
    /// <see cref="GetOrLoad"/>, and a claim on the folder's lifetime. Every call must be matched
    /// by a <see cref="Release"/>.
    /// </summary>
    TSnapshot Acquire(string folder);

    /// <summary>
    /// Gives up one claim. When the last one goes, the watcher is disposed and the snapshot
    /// dropped; the next ask reads the folder afresh.
    /// </summary>
    void Release(string folder);

    /// <summary>
    /// Calls <paramref name="onChanged"/> with each new snapshot of <paramref name="folder"/>,
    /// until the subscription it returns is disposed; disposing it twice is harmless, and once it
    /// is disposed no call follows.
    /// </summary>
    IDisposable Watch(string folder, Action<TSnapshot> onChanged);
}
