using System.Reflection;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Files.Tests.Support;

/// <summary>What a test makes a <see cref="FileSystemWatcher"/> do that a real file system will not on demand.</summary>
public static class FileSystemWatchers
{
    /// <summary>
    /// Raises the watcher's Error event with the buffer overflow the operating system reports when
    /// it dropped events, through the same protected method the watcher itself calls.
    /// </summary>
    public static void RaiseOverflow(this FileSystemWatcher watcher) =>
        (typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FileSystemWatcher.OnError was not found."))
        .Invoke(watcher, [new ErrorEventArgs(new InternalBufferOverflowException())]);

    /// <summary>
    /// Raises the watcher's Renamed event for a move from <paramref name="oldPath"/> to
    /// <paramref name="newPath"/>, as the watcher does when it pairs the two halves of a move.
    /// </summary>
    public static void RaiseRenamed(this FileSystemWatcher watcher, string oldPath, string newPath) =>
        Raise(watcher, "OnRenamed", new RenamedEventArgs(WatcherChangeTypes.Renamed, DirectoryOf(newPath), IoPath.GetFileName(newPath), IoPath.GetFileName(oldPath)));

    /// <summary>
    /// Raises a move as the Linux watcher reports it when its two halves arrive further apart than
    /// the watcher's 2 ms poll: a Deleted of <paramref name="oldPath"/>, then a Created of
    /// <paramref name="newPath"/> (see <see cref="SplitMoveJoiner"/>).
    /// </summary>
    public static void RaiseSplitMove(this FileSystemWatcher watcher, string oldPath, string newPath)
    {
        Raise(watcher, "OnDeleted", new FileSystemEventArgs(WatcherChangeTypes.Deleted, DirectoryOf(oldPath), IoPath.GetFileName(oldPath)));
        Raise(watcher, "OnCreated", new FileSystemEventArgs(WatcherChangeTypes.Created, DirectoryOf(newPath), IoPath.GetFileName(newPath)));
    }

    /// <summary>The watcher behind <paramref name="folder"/>, for a test that has to make it overflow or stop it hearing.</summary>
    public static FileSystemWatcher Watcher(this WatchedFolder folder) => folder.Watcher;

    /// <summary>The watcher of <paramref name="root"/>, or null while it has none - for a test that has to make it overflow.</summary>
    public static FileSystemWatcher? Watcher(this ProjectFileEvents events, string root) => events.WatcherOf(root);

    /// <summary>How many project roots <paramref name="events"/> watches now, for a test of the one watcher per root.</summary>
    public static int WatchedRoots(this ProjectFileEvents events) => events.Roots;

    /// <summary>The watcher behind <paramref name="file"/>; the file's folder must have existed when it was made, outside every watched project root.</summary>
    public static FileSystemWatcher Watcher(this WatchedFile file) =>
        file.Folder?.Watcher ?? throw new InvalidOperationException($"{file.Path} has no watcher of its own: its folder did not exist, or it is heard through its project root.");

    /// <summary>Whether <paramref name="file"/> made a watcher of its own rather than being heard through its project root's.</summary>
    public static bool HasWatcherOfItsOwn(this WatchedFile file) => file.Folder is not null;

    /// <summary>The watcher behind <paramref name="tree"/>; the tree must have been made outside every watched project root.</summary>
    public static FileSystemWatcher Watcher(this WatchedTree tree) =>
        tree.Folder?.Watcher ?? throw new InvalidOperationException($"{tree.Path} has no watcher of its own: it is heard through its project root.");

    /// <summary>Whether <paramref name="tree"/> made a watcher of its own rather than being heard through its project root's.</summary>
    public static bool HasWatcherOfItsOwn(this WatchedTree tree) => tree.Folder is not null;

    private static void Raise(FileSystemWatcher watcher, string method, EventArgs arguments) =>
        (typeof(FileSystemWatcher).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"FileSystemWatcher.{method} was not found."))
        .Invoke(watcher, [arguments]);

    private static string DirectoryOf(string path) =>
        IoPath.GetDirectoryName(path) ?? throw new ArgumentException($"{path} has no folder.", nameof(path));
}
