using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Watches the files one solution's graph was derived from, and says when the graph is stale.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is watched is what was read</b> - the solution, every project file it resolved, and
/// every <c>Directory.Packages.props</c> above them, as
/// <see cref="DependencyGraphStore.WatchedFiles"/> reports them. A watcher over only the
/// solution would miss a reference added to a project; a watcher over only project files would
/// miss a version bump, which in a centrally managed repository lands in a props file and in no
/// <c>.csproj</c> at all.
/// </para>
/// <para>
/// <b>The watch is per directory, filtered per file.</b> One <see cref="FileSystemWatcher"/>
/// per directory containing something interesting, rather than one per file: a solution of
/// thirty projects would otherwise open thirty handles for what three directories cover, and a
/// process has a finite number of them.
/// </para>
/// <para>
/// <b>Changes settle before they are reported.</b> A build or a package restore rewrites many
/// files in a burst, and a graph rebuilt per file-system event would rebuild dozens of times for
/// one logical change. The settle delay collapses a burst into one notification.
/// </para>
/// </remarks>
public sealed class SolutionWatcher : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<SolutionWatcher>();

    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(250);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _files;
    private readonly TimeSpan _settleDelay;
    private readonly Lock _gate = new();

    private Timer? _settleTimer;
    private bool _disposed;

    public SolutionWatcher(IReadOnlyList<string> files, TimeSpan? settleDelay = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        _files = new HashSet<string>(files.Select(IoPath.GetFullPath), StringComparer.OrdinalIgnoreCase);
        _settleDelay = settleDelay ?? DefaultSettleDelay;

        foreach (var directory in _files
            .Select(file => IoPath.GetDirectoryName(file))
            .Where(directory => !string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            .Select(directory => directory!)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var watcher = new FileSystemWatcher(directory)
            {
                // Not recursive: the files this graph was derived from are named exactly, and a
                // recursive watch over a repository root would wake on every build artifact.
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            watcher.Changed += OnFileSystemEvent;
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;

            // Error is the only signal FileSystemWatcher gives when its internal buffer overflows
            // and it has silently dropped events. Unsubscribed, an overflow looks exactly like a
            // quiet solution - and LOGGED ONLY, it still does, because the graph is never marked
            // stale. Which watched file changed is unknowable, so the graph goes stale as though one
            // had (backend-centralization R2.9).
            watcher.Error += (_, args) =>
            {
                _logger.Warning(args.GetException(), "The watcher for {Directory} stumbled; treating the graph as stale", directory);
                ScheduleSettle();
            };

            // Enabled AFTER the handlers, as RootFolderWatcher and the two WatchedFolders do:
            // a watcher live before its handlers exist raises into nothing.
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        _logger.Debug(
            "Watching {Files} files across {Directories} directories for solution changes",
            _files.Count,
            _watchers.Count);
    }

    /// <summary>Raised once a burst of changes to the watched files has settled.</summary>
    public event EventHandler? Stale;

    private void OnFileSystemEvent(object sender, FileSystemEventArgs args)
    {
        // Only the files the graph was actually derived from. A directory holds plenty this
        // graph does not read - build output above all - and waking on those would rebuild the
        // graph for changes that cannot affect it.
        if (!_files.Contains(IoPath.GetFullPath(args.FullPath)))
        {
            return;
        }

        ScheduleSettle();
    }

    // One settle per burst: a change to a watched file, or an overflow that may have hidden one.
    private void ScheduleSettle()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _settleTimer?.Dispose();
            _settleTimer = new Timer(_ => Settle(), null, _settleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Settle()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _settleTimer?.Dispose();
            _settleTimer = null;
        }

        Stale?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _settleTimer?.Dispose();
            _settleTimer = null;
        }

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileSystemEvent;
            watcher.Created -= OnFileSystemEvent;
            watcher.Deleted -= OnFileSystemEvent;
            watcher.Renamed -= OnFileSystemEvent;
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
