using System.Reflection;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.TestSupport;

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

    /// <summary>The watcher behind <paramref name="folder"/>, for a test that has to make it overflow or stop it hearing.</summary>
    public static FileSystemWatcher Watcher(this WatchedFolder folder) => folder.Watcher;

    /// <summary>The watcher of <paramref name="root"/>, or null while it has none - for a test that has to make it overflow.</summary>
    public static FileSystemWatcher? Watcher(this ProjectFileEvents events, string root) => events.WatcherOf(root);

    /// <summary>How many project roots <paramref name="events"/> watches now, for a test of the one watcher per root.</summary>
    public static int WatchedRoots(this ProjectFileEvents events) => events.Roots;

    /// <summary>The watcher behind <paramref name="file"/>; the file's folder must have existed when it was made.</summary>
    public static FileSystemWatcher Watcher(this WatchedFile file) =>
        file.Folder?.Watcher ?? throw new InvalidOperationException($"{file.Path} is not watched: its folder did not exist.");
}
