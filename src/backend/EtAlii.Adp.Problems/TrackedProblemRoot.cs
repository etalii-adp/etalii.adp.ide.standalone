using Serilog;

// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>One tracked project: its watcher, and the burst of paths waiting to settle.</summary>
internal sealed class TrackedProblemRoot : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<TrackedProblemRoot>();

    private readonly ProblemMaintenance _owner;
    private readonly FileSystemWatcher _watcher;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; }

    public TrackedProblemRoot(string rootPath, ProblemMaintenance owner)
    {
        Path = rootPath;
        _owner = owner;
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            // Unlike the hierarchy's watcher, content matters here: an edit that renames
            // nothing still invalidates a verdict.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        // Guarded like RootFolderWatcher's dispatch: these run on the watcher's own
        // thread, where an unhandled exception is the death of the process. One change
        // that cannot be applied loses that change and says so.
        _watcher.Created += (_, e) => Guarded(() => owner.OnCreatedOrChanged(this, e.FullPath), e.FullPath);
        _watcher.Changed += (_, e) => Guarded(() => owner.OnCreatedOrChanged(this, e.FullPath), e.FullPath);
        _watcher.Deleted += (_, e) => Guarded(() => owner.OnDeleted(this, e.FullPath), e.FullPath);
        _watcher.Renamed += (_, e) => Guarded(() => owner.OnRenamed(this, e.OldFullPath, e.FullPath), e.FullPath);
        _watcher.Error += (_, e) => _logger.Warning(e.GetException(), "The problem watcher for {RootPath} stumbled", rootPath);

        _watcher.EnableRaisingEvents = true;
    }

    public void Enqueue(string path)
    {
        lock (_gate)
        {
            _pending.Add(path);
        }

        // Rapid changes coalesce: the window starts over until the burst settles.
        _owner.Settling.Schedule(this);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _owner.Settling.Cancel(this);
        lock (_gate)
        {
            _pending.Clear();
        }
    }

    /// <summary>The burst has settled: hand everything it touched to one re-validation.</summary>
    internal void Settle()
    {
        string[] paths;
        lock (_gate)
        {
            paths = _pending.ToArray();
            _pending.Clear();
        }
        if (paths.Length == 0)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await _owner.RevalidateAsync(this, paths);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Re-validating {Count} changed files under {RootPath} failed; the next change tries again", paths.Length, Path);
            }
        });
    }

    private static void Guarded(Action apply, string path)
    {
        try
        {
            apply();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Applying a watcher event for {Path} failed; the change is lost until the next validation", path);
        }
    }
}
