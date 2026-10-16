using Serilog;

namespace EtAlii.Adp.Documents;

/// <summary>
/// One watched subject of a <see cref="WatchedFolderStore{TSnapshot}"/>: what it listens to, its
/// settle timer, its claim count and the snapshot last read from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two shapes, one settle.</b> With no files named, one watcher covers the folder and everything
/// under it. With files named, there is one watcher per directory holding one of them, not
/// recursive, and an event counts only when it names one of the files: a solution's projects sit in
/// directories full of build output, and a graph rebuilt for that would rebuild for nothing.
/// </para>
/// <para>
/// <b>A folder inside a watched project listens to the project.</b> Where a project root that holds
/// the folder is watched already (<see cref="ProjectFileEvents"/>), the folder picks its own events
/// out of that root's rather than making a second watcher over the same files; elsewhere it makes a
/// <see cref="WatchedFolder"/> of its own (code-structure FR-050).
/// </para>
/// <para>
/// Content filters as well as name ones, a timer on the store's clock that restarts until a burst
/// settles, and <b>every handler guarded</b>. These run on the watcher's own thread, where an unhandled exception is not
/// a failed request but the death of the process.
/// </para>
/// <para>
/// <b>An overflow is treated as a change.</b> <c>Error</c> is the only signal a watcher gives when
/// its buffer overflowed and it dropped events; which file changed is unknowable, so the subject is
/// read again as though one had (backend-centralization R2.9). Logged only, an overflow looks
/// exactly like a quiet folder.
/// </para>
/// </remarks>
internal sealed class WatchedFolderEntry<TSnapshot> : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<WatchedFolderEntry<TSnapshot>>();

    private readonly Action<WatchedFolderEntry<TSnapshot>> _onSettled;
    private readonly Lock _gate = new();
    private readonly TimeSpan _settleDelay;
    private readonly ProjectFileEvents? _events;
    private readonly TimeProvider _time;
    private List<Listening> _watchers = [];

    /// <summary>The files an event must name to count, or null when anything under the folder does.</summary>
    private volatile HashSet<string>? _files;

    private ITimer? _settleTimer;
    private int _claims;
    private bool _disposed;

    public WatchedFolderEntry(string path, TSnapshot snapshot, IReadOnlyCollection<string>? files, TimeSpan settleDelay, Action<WatchedFolderEntry<TSnapshot>> onSettled, ProjectFileEvents? events, TimeProvider time)
    {
        Path = path;
        Snapshot = snapshot;
        _settleDelay = settleDelay;
        _onSettled = onSettled;
        _events = events;
        _time = time;

        Watch(files);
    }

    public string Path { get; }

    /// <summary>The last read of this subject. Replaced wholesale; never mutated in place.</summary>
    public TSnapshot Snapshot { get; set; }

    /// <summary>The watchers as they stand, for a test that has to make one overflow.</summary>
    internal IReadOnlyList<FileSystemWatcher> Watchers
    {
        get
        {
            lock (_gate)
            {
                return [.. _watchers.Select(watched => watched.Watcher()).OfType<FileSystemWatcher>()];
            }
        }
    }

    /// <summary>
    /// Points the watchers at <paramref name="files"/>, or at the whole folder when there are none.
    /// Nothing changes when they already point there; otherwise the new watchers are live before
    /// the old ones go, so the swap has no window in which nothing listens.
    /// </summary>
    public void Watch(IReadOnlyCollection<string>? files)
    {
        var wanted = files is null
            ? null
            : new HashSet<string>(files.Select(System.IO.Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        List<Listening> previous;
        lock (_gate)
        {
            if (_disposed || (_watchers.Count > 0 && SameFiles(_files, wanted)))
            {
                return;
            }

            _files = wanted;
            previous = _watchers;
            _watchers = wanted is null ? [Listen(Path, recursive: true)] : [.. Directories(wanted).Select(directory => Listen(directory, recursive: false))];
        }

        foreach (var watcher in previous)
        {
            watcher.Handle.Dispose();
        }
    }

    public void Claim()
    {
        lock (_gate)
        {
            _claims++;
        }
    }

    /// <summary>Gives up one claim; true when that was the last one.</summary>
    public bool Unclaim()
    {
        lock (_gate)
        {
            _claims = Math.Max(0, _claims - 1);
            return _claims == 0;
        }
    }

    public void Dispose()
    {
        List<Listening> watchers;
        lock (_gate)
        {
            _disposed = true;
            _settleTimer?.Dispose();
            _settleTimer = null;
            watchers = _watchers;
            _watchers = [];
        }

        foreach (var watcher in watchers)
        {
            watcher.Handle.Dispose();
        }
    }

    /// <summary>
    /// Listens to <paramref name="directory"/> through the watched project root that holds it, or
    /// through a watcher of its own where none does.
    /// </summary>
    private Listening Listen(string directory, bool recursive)
    {
        if (_events?.SubscribeWithin(directory, heard => HeardWithin(heard, directory, recursive)) is { } shared)
        {
            var events = _events;
            return new Listening(shared.Subscription, () => events.WatcherOf(shared.Root));
        }

        var folder = new WatchedFolder(directory, Heard, recursive);
        return new Listening(folder, () => folder.Watcher);
    }

    /// <summary>An event of the whole project root, heard only when it concerns <paramref name="directory"/>.</summary>
    private void HeardWithin(FileEvent heard, string directory, bool recursive)
    {
        if (heard.Kind == FileEventKind.Lost || Holds(heard.Path) || (heard.OldPath is { } oldPath && Holds(oldPath)))
        {
            Heard(heard);
        }

        bool Holds(string path) => recursive
            ? ProjectFileEvents.IsWithin(path, directory)
            : string.Equals(System.IO.Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase);
    }

    private void Heard(FileEvent heard)
    {
        if (heard.Kind == FileEventKind.Lost)
        {
            // Which file changed is unknowable, so the subject is read again as though one had.
            Schedule(heard.Path);
            return;
        }

        // Either name counts: a watched file renamed away is as much a change as one renamed in.
        Guarded(heard.Path, heard.OldPath);
    }

    private void Guarded(string path, string? oldPath = null)
    {
        // Only the files the snapshot was read from, when it names them.
        var files = _files;
        if (files is not null && !Names(files, path) && (oldPath is null || !Names(files, oldPath)))
        {
            return;
        }

        Schedule(path);
    }

    private void Schedule(string path)
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
                _settleTimer = _time.CreateTimer(_ => Settle(), null, _settleDelay, Timeout.InfiniteTimeSpan);
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

    private static bool Names(HashSet<string> files, string path) => files.Contains(System.IO.Path.GetFullPath(path));

    private static bool SameFiles(HashSet<string>? current, HashSet<string>? wanted) =>
        current is null ? wanted is null : wanted is not null && current.SetEquals(wanted);

    /// <summary>What the entry listens to, and the watcher behind it for a test that has to make it overflow.</summary>
    private sealed record Listening(IDisposable Handle, Func<FileSystemWatcher?> Watcher);

    /// <summary>One per directory holding a named file, rather than one per file: thirty projects in three directories are three handles.</summary>
    private static IEnumerable<string> Directories(HashSet<string> files) =>
        files
            .Select(System.IO.Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            .Select(directory => directory!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
