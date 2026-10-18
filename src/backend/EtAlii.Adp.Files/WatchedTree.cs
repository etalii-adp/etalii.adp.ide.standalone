namespace EtAlii.Adp.Files;

/// <summary>
/// A folder and everything under it, heard through the watched project root that holds it
/// (<see cref="ProjectFileEvents"/>), or through a recursive <see cref="WatchedFolder"/> of its own
/// where no watched root does (code-structure FR-050, T280). For an owner of a folder tree inside a
/// project - a module's open folder - that would otherwise put a second recursive watcher over a tree
/// the project's already covers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the folder's events.</b> Through the root everything under the root is heard; an event is
/// passed on when its path, or the old name of a rename, is the folder or lies under it.
/// </para>
/// <para>
/// <b>A Lost is passed on as it came</b>, for the root or for the folder: which file changed is then
/// unknowable either way, and only the owner knows what reading again means.
/// </para>
/// </remarks>
public sealed class WatchedTree : IDisposable
{
    private readonly IDisposable _listening;

    /// <param name="folder">The folder; it must exist.</param>
    /// <param name="heard">What runs for each event of the folder, on the watcher's thread.</param>
    /// <param name="events">The project roots' event sources; without them the folder gets its own watcher.</param>
    public WatchedTree(string folder, Action<FileEvent> heard, ProjectFileEvents? events = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(heard);
        Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));

        if (events?.SubscribeWithin(Path, within => HeardWithin(within, heard)) is { } shared)
        {
            _listening = shared.Subscription;
            return;
        }

        Folder = new WatchedFolder(Path, heard, recursive: true);
        _listening = Folder;
    }

    /// <summary>The folder, in full.</summary>
    public string Path { get; }

    /// <summary>The folder's own watcher, or null when it is heard through its project root; for a test that has to reach the watcher.</summary>
    internal WatchedFolder? Folder { get; }

    public void Dispose() => _listening.Dispose();

    private void HeardWithin(FileEvent within, Action<FileEvent> heard)
    {
        if (within.Kind == FileEventKind.Lost
            || ProjectFileEvents.IsWithin(within.Path, Path)
            || (within.OldPath is { } oldPath && ProjectFileEvents.IsWithin(oldPath, Path)))
        {
            heard(within);
        }
    }
}
