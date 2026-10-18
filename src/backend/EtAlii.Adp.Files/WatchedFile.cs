using Serilog;

namespace EtAlii.Adp.Files;

/// <summary>
/// One file watched for what other programs do to it, with an asynchronous read-retry on a
/// <see cref="TimeProvider"/> (code-structure FR-050). Every event, an overflow included, asks for
/// the owner's re-read; the re-reads run one at a time, in order, off the watcher's thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>One at a time, and never one too few.</b> A notification arriving while a re-read runs
/// queues one more, and every notification arriving before that one starts joins it: a burst of
/// events is read twice at most, and the last read always starts after the last event. Two re-reads
/// running at once could finish out of order and leave the older content showing.
/// </para>
/// <para>
/// <b>A refusal must not consume the change.</b> A publish is a temp-then-replace, and a read that
/// lands while the name is in transit, or while the writer still holds the file, is refused. Read
/// once, that refusal throws away the only notification for the write, so
/// <see cref="ReadAsync{T}"/> tries <see cref="ReadAttempts"/> times, <see cref="BetweenReadAttempts"/>
/// apart. A retry narrows the window and does not close it: a hold longer than the attempts still
/// loses the change. The wait is a <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>,
/// so no thread sleeps through it and a test can advance it.
/// </para>
/// </remarks>
public sealed class WatchedFile : IDisposable
{
    /// <summary>How many times a read is tried before it is given up on.</summary>
    public const int ReadAttempts = 3;

    /// <summary>How long between two attempts of a read.</summary>
    public static readonly TimeSpan BetweenReadAttempts = TimeSpan.FromMilliseconds(20);

    private static readonly ILogger _logger = Log.ForContext<WatchedFile>();

    private readonly Func<Task> _changed;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    /// <summary>The last re-read asked for; the next one starts after it.</summary>
    private Task _tail = Task.CompletedTask;

    /// <summary>The re-read asked for and not started yet, which every notification until it starts joins.</summary>
    private TaskCompletionSource? _queued;

    private bool _disposed;

    /// <summary>The subscription to the watched project root that holds the file, when it is heard through that root.</summary>
    private readonly IDisposable? _shared;

    /// <param name="path">The file.</param>
    /// <param name="changed">The owner's re-read: what runs, one at a time, after the file was changed by another program.</param>
    /// <param name="time">The clock a read's retry waits on.</param>
    /// <param name="events">
    /// The project roots' event sources: a file inside a root already watched is heard through that root's
    /// one watcher rather than a watcher of its own (code-structure T280). Without them, or outside every
    /// watched root, the file's folder gets its own.
    /// </param>
    public WatchedFile(string path, Func<Task> changed, TimeProvider time, ProjectFileEvents? events = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(time);
        Path = path;
        _changed = changed;
        _time = time;

        // A file in a folder that does not exist is read and not watched: there is nothing a watcher could be put on.
        var directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var file = System.IO.Path.GetFullPath(path);
        if (events?.SubscribeWithin(directory, heard => HeardWithin(heard, file)) is { } shared)
        {
            _shared = shared.Subscription;
            return;
        }

        Folder = new WatchedFolder(directory, _ => Notify(), filter: System.IO.Path.GetFileName(path));
    }

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>The folder watcher, or null when the file's folder did not exist or the file is heard through its project root; for a test that has to reach the watcher.</summary>
    internal WatchedFolder? Folder { get; }

    /// <summary>
    /// Asks for the owner's re-read, as an event does: now when none is running, else once after the
    /// running one. Completes when a re-read that started after this call has ended.
    /// </summary>
    public Task Notify()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_queued is not null)
            {
                return _queued.Task;
            }

            var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queued = run;
            var previous = _tail;
            _tail = run.Task;

            // On the pool, never inline: the watcher's thread is not held for a read and its retries.
            _ = previous.ContinueWith(_ => RunAsync(run), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return run.Task;
        }
    }

    /// <summary>
    /// Reads with <paramref name="read"/> until <paramref name="succeeded"/> says it did or
    /// <see cref="ReadAttempts"/> are spent, waiting <see cref="BetweenReadAttempts"/> on this file's
    /// clock between two; the last result and how many attempts it took.
    /// </summary>
    public Task<(T Result, int Attempts)> ReadAsync<T>(Func<string, T> read, Func<T, bool> succeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        return RetryAsync(_ => Task.FromResult(read(Path)), succeeded, ReadAttempts, BetweenReadAttempts, _time, cancellationToken);
    }

    /// <summary>
    /// Reads with <paramref name="read"/> until <paramref name="succeeded"/> says it did or
    /// <paramref name="attempts"/> are spent, waiting <paramref name="between"/> on
    /// <paramref name="time"/> between two; the last result and how many attempts it took.
    /// </summary>
    /// <remarks>
    /// The one read-retry of the backend: a watched file's re-read and a document's load both run
    /// through it, each with its own count and interval, and no thread sleeps through the wait
    /// (code-structure FR-050).
    /// </remarks>
    public static async Task<(T Result, int Attempts)> RetryAsync<T>(
        Func<CancellationToken, Task<T>> read,
        Func<T, bool> succeeded,
        int attempts,
        TimeSpan between,
        TimeProvider time,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(succeeded);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        ArgumentNullException.ThrowIfNull(time);

        for (var attempt = 1; ; attempt++)
        {
            var result = await read(cancellationToken).ConfigureAwait(false);
            if (succeeded(result) || attempt >= attempts)
            {
                return (result, attempt);
            }

            await Task.Delay(between, time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Stops watching; a re-read already running finishes, and none starts after it.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        Folder?.Dispose();
        _shared?.Dispose();
    }

    /// <summary>An event of the whole project root, heard only when it concerns this file; an overflow concerns every file.</summary>
    private void HeardWithin(FileEvent heard, string file)
    {
        if (heard.Kind == FileEventKind.Lost || Names(heard.Path) || (heard.OldPath is { } oldPath && Names(oldPath)))
        {
            _ = Notify();
        }

        bool Names(string path) => string.Equals(path, file, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunAsync(TaskCompletionSource run)
    {
        bool disposed;
        lock (_gate)
        {
            // Started: a notification from here on asks for the next one.
            if (ReferenceEquals(_queued, run))
            {
                _queued = null;
            }

            disposed = _disposed;
        }

        try
        {
            if (!disposed)
            {
                await _changed().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Reading {Path} again after a change failed; the next change tries again", Path);
        }
        finally
        {
            run.SetResult();
        }
    }
}
