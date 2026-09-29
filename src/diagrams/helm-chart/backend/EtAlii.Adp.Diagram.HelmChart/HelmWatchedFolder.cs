using Serilog;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One watched folder: its <see cref="FileSystemWatcher"/>, its settle timer, its claim count
/// and the chart last read from it.
/// </summary>
/// <remarks>
/// The shape <c>TrackedProblemRoot</c> established, copied rather than reinvented: subdirectories
/// included, content filters as well as name ones, a timer that restarts until a burst settles,
/// and - the part this repository has already paid for once - <b>every handler guarded</b>. These
/// run on the watcher's own thread, where an unhandled exception is not a failed request but the
/// death of the process.
/// </remarks>
internal sealed class HelmWatchedFolder : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<HelmWatchedFolder>();

    private readonly Action<HelmWatchedFolder> _onSettled;
    private readonly FileSystemWatcher _watcher;
    private readonly Lock _gate = new();
    private readonly TimeSpan _settleDelay;
    private Timer? _settleTimer;
    private bool _disposed;

    public HelmWatchedFolder(string path, HelmChart chart, TimeSpan settleDelay, Action<HelmWatchedFolder> onSettled)
    {
        Path = path;
        Chart = chart;
        _settleDelay = settleDelay;
        _onSettled = onSettled;

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            // Content matters as much as names here: editing a role's meta changes what the
            // diagram means without renaming anything.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        _watcher.Created += (_, e) => Guarded(e.FullPath);
        _watcher.Changed += (_, e) => Guarded(e.FullPath);
        _watcher.Deleted += (_, e) => Guarded(e.FullPath);
        _watcher.Renamed += (_, e) => Guarded(e.FullPath);
        _watcher.Error += (_, e) => _logger.Warning(e.GetException(), "The watcher for {Path} stumbled", path);

        _watcher.EnableRaisingEvents = true;
    }

    public string Path { get; }

    /// <summary>The last read of this folder. Replaced wholesale; never mutated in place.</summary>
    public HelmChart Chart { get; set; }

    /// <summary>How many sessions are holding this folder open. The watcher outlives the count reaching zero by nothing.</summary>
    public int Claims { get; private set; }

    public void Claim()
    {
        lock (_gate)
        {
            Claims++;
        }
    }

    /// <summary>Gives up one claim; true when that was the last one.</summary>
    public bool Unclaim()
    {
        lock (_gate)
        {
            Claims = Math.Max(0, Claims - 1);
            return Claims == 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _settleTimer?.Dispose();
            _settleTimer = null;
        }
        _watcher.Dispose();
    }

    private void Guarded(string path)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                // Rapid changes coalesce: a multi-file save costs one re-read, not one each.
                _settleTimer?.Dispose();
                _settleTimer = new Timer(_ => Settle(), null, _settleDelay, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Scheduling a re-read of {Path} after a change to {Changed} failed", Path, path);
        }
    }

    private void Settle()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }
            _onSettled(this);
        }
        catch (Exception exception)
        {
            // One re-read that cannot be applied loses that re-read and says so; the next
            // change catches the folder up.
            _logger.Error(exception, "Re-reading {Path} failed", Path);
        }
    }
}
