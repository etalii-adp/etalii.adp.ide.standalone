using Serilog;

namespace EtAlii.Adp.Backend.Hierarchy;

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

    public RootFolderWatcher(string rootPath, ChangeCallback onChange, Action<Exception> onError)
    {
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };

        // Guarded: these run on the watcher's own thread, where an unhandled exception is not
        // a failed request but the death of the whole process (found by the
        // diagram-workspace-tabs manual pass, when a handler probed a file a multi-file
        // delete had already taken). One change that cannot be applied loses that change and
        // says so; the model's next reconcile catches it up.
        _watcher.Created += (_, e) => Dispatch(onChange, WatcherChangeTypes.Created, null, e.FullPath);
        _watcher.Deleted += (_, e) => Dispatch(onChange, WatcherChangeTypes.Deleted, e.FullPath, null);
        _watcher.Renamed += (_, e) => Dispatch(onChange, WatcherChangeTypes.Renamed, e.OldFullPath, e.FullPath);
        _watcher.Error += (_, e) => onError(e.GetException());

        _watcher.EnableRaisingEvents = true;
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

    public void Dispose() => _watcher.Dispose();
}
