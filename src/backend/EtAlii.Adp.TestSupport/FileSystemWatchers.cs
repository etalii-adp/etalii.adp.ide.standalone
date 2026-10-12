using System.Reflection;

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
}
