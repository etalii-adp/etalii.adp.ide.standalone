namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Thin wrapper around a single <see cref="FileSystemWatcher"/> instance,
/// dedicated to one connection's project root folder. Two connections to the
/// same project each get their own instance; nothing is shared between them.
/// </summary>
public sealed class RootFolderWatcher : IDisposable
{
    public delegate void ChangeCallback(WatcherChangeTypes changeType, string? oldPath, string? newPath);

    private readonly FileSystemWatcher _watcher;

    public RootFolderWatcher(string rootPath, ChangeCallback onChange, Action<Exception> onError)
    {
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };

        _watcher.Created += (_, e) => onChange(WatcherChangeTypes.Created, null, e.FullPath);
        _watcher.Deleted += (_, e) => onChange(WatcherChangeTypes.Deleted, e.FullPath, null);
        _watcher.Renamed += (_, e) => onChange(WatcherChangeTypes.Renamed, e.OldFullPath, e.FullPath);
        _watcher.Error += (_, e) => onError(e.GetException());

        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose() => _watcher.Dispose();
}
