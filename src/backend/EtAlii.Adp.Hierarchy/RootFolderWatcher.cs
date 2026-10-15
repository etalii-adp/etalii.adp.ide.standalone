using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Thin wrapper around a single <see cref="FileSystemWatcher"/> instance,
/// dedicated to one connection's project root folder. Two connections to the
/// same project each get their own instance; nothing is shared between them.
/// </summary>
public sealed class RootFolderWatcher : IDisposable
{
    public delegate void ChangeCallback(WatcherChangeTypes changeType, string? oldPath, string? newPath);

    private static readonly ILogger _logger = Log.ForContext<RootFolderWatcher>();

    private readonly FileSystemWatcher _watcher;
    private readonly SplitMoveJoiner? _joiner;

    public RootFolderWatcher(string rootPath, ChangeCallback onChange, Action<Exception> onError, bool includeContentChanges = false)
    {
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = includeContentChanges
                // The reload bridge cares what a file says, not only what it is called: an
                // in-place write raises no name event at all, so it needs LastWrite and Size.
                ? NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                : NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };

        // Guarded: these run on the watcher's own thread, where an unhandled exception is not
        // a failed request but the death of the whole process (found by the
        // diagram-workspace-tabs manual pass, when a handler probed a file a multi-file
        // delete had already taken). One change that cannot be applied loses that change and
        // says so; the model's next reconcile catches it up.
        // Off Windows a move can arrive split into a Deleted and a Created, which would cost the
        // moved entry its id and a selection on it its existence; the joiner puts such a move
        // back together. Windows reports every move as one Renamed, so there a Deleted followed
        // by a Created is exactly that, and is delivered as it came.
        _joiner = OperatingSystem.IsWindows() ? null : new SplitMoveJoiner(Deliver);
        Action<FileEvent> receive = _joiner is null ? Deliver : _joiner.Post;

        _watcher.Created += (_, e) => receive(new FileEvent(FileEventKind.Created, e.FullPath));
        _watcher.Deleted += (_, e) => receive(new FileEvent(FileEventKind.Deleted, e.FullPath));
        _watcher.Renamed += (_, e) => receive(new FileEvent(FileEventKind.Renamed, e.FullPath, e.OldFullPath));
        _watcher.Error += (_, e) =>
        {
            // A deletion still held for its move is older than whatever the error recovers.
            _joiner?.Flush();
            onError(e.GetException());
        };
        if (includeContentChanges)
        {
            _watcher.Changed += (_, e) => receive(new FileEvent(FileEventKind.Changed, e.FullPath));
        }

        _watcher.EnableRaisingEvents = true;

        return;

        void Deliver(FileEvent heard) => Dispatch(onChange, heard);
    }

    /// <summary>Hands one event to the callback in the shape the hierarchy model takes it.</summary>
    internal static void Dispatch(ChangeCallback onChange, FileEvent heard)
    {
        // Lost never comes this way: this watcher reports its stumbles through onError.
        switch (heard.Kind)
        {
            case FileEventKind.Created:
                Dispatch(onChange, WatcherChangeTypes.Created, null, heard.Path);
                break;
            case FileEventKind.Changed:
                Dispatch(onChange, WatcherChangeTypes.Changed, null, heard.Path);
                break;
            case FileEventKind.Deleted:
                Dispatch(onChange, WatcherChangeTypes.Deleted, heard.Path, null);
                break;
            case FileEventKind.Renamed:
                Dispatch(onChange, WatcherChangeTypes.Renamed, heard.OldPath, heard.Path);
                break;
        }
    }

    private static void Dispatch(ChangeCallback onChange, WatcherChangeTypes changeType, string? oldPath, string? newPath)
    {
        try
        {
            onChange(changeType, oldPath, newPath);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Applying a {ChangeType} watcher event for {Path} failed; the change is lost until the next reconcile", changeType, newPath ?? oldPath);
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _joiner?.Dispose();
    }
}
