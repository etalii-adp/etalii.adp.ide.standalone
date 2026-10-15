using Serilog;

namespace EtAlii.Adp.Documents;

/// <summary>
/// A folder watched for what other programs do to it: one <see cref="FileSystemWatcher"/>,
/// subscribed to every event a write raises before it is enabled, with every handler guarded
/// (code-structure FR-050). The one place the tree makes a watcher; <see cref="WatchedFile"/>,
/// the folder store and the problems watcher all watch through it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every event a publish raises.</b> A write in place raises Changed; a temp-then-replace
/// publish, which is what <c>AdpFileWriter</c> does, raises Renamed as the scratch file takes the
/// destination's name, Created where there was nothing before, and can present the destination's
/// transition as a Deleted. An owner subscribed to Changed alone never hears an external save.
/// </para>
/// <para>
/// <b>An overflow is an event.</b> <c>Error</c> is the only signal a watcher gives when its
/// buffer overflowed and it dropped events. It is logged here and passed on as
/// <see cref="FileEventKind.Lost"/>, because only the owner knows what reading again means;
/// logged only, an overflow looks exactly like a quiet folder (backend-centralization R2.9).
/// </para>
/// <para>
/// <b>Subscribed before enabled.</b> A watcher enabled first is live with no handlers attached, and
/// anything raised in that window is received by nobody.
/// </para>
/// <para>
/// <b>Guarded.</b> The handlers run on the watcher's own thread, where an unhandled exception is not
/// a failed request but the death of the process.
/// </para>
/// </remarks>
public sealed class WatchedFolder : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<WatchedFolder>();

    private readonly Action<FileEvent> _heard;

    /// <param name="path">The folder; it must exist.</param>
    /// <param name="heard">What runs for each event, on the watcher's thread; an exception it throws is logged.</param>
    /// <param name="recursive">Whether everything under the folder is watched, or only the folder itself.</param>
    /// <param name="filter">The one name watched, or null for every name.</param>
    public WatchedFolder(string path, Action<FileEvent> heard, bool recursive = false, string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(heard);
        _heard = heard;

        Watcher = filter is null ? new FileSystemWatcher(path) : new FileSystemWatcher(path, filter);
        Watcher.IncludeSubdirectories = recursive;

        // Content matters as much as names: editing a file changes what it means without renaming anything.
        Watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;

        Watcher.Created += (_, e) => Hear(new FileEvent(FileEventKind.Created, e.FullPath));
        Watcher.Changed += (_, e) => Hear(new FileEvent(FileEventKind.Changed, e.FullPath));
        Watcher.Deleted += (_, e) => Hear(new FileEvent(FileEventKind.Deleted, e.FullPath));
        Watcher.Renamed += (_, e) => Hear(new FileEvent(FileEventKind.Renamed, e.FullPath, e.OldFullPath));
        Watcher.Error += (_, e) =>
        {
            _logger.Warning(e.GetException(), "The watcher for {Path} stumbled; events may have been lost", path);
            Hear(new FileEvent(FileEventKind.Lost, path));
        };

        Watcher.EnableRaisingEvents = true;
    }

    /// <summary>The watcher itself, for a test that has to make it overflow or stop it hearing.</summary>
    internal FileSystemWatcher Watcher { get; }

    public void Dispose() => Watcher.Dispose();

    private void Hear(FileEvent heard)
    {
        try
        {
            _heard(heard);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Handling a watcher event for {Path} failed; the change is lost until the next one", heard.Path);
        }
    }
}
