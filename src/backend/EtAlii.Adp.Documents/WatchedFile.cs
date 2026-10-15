using Serilog;

namespace EtAlii.Adp.Documents;

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

    /// <param name="path">The file.</param>
    /// <param name="changed">The owner's re-read: what runs, one at a time, after the file was changed by another program.</param>
    /// <param name="time">The clock a read's retry waits on.</param>
    public WatchedFile(string path, Func<Task> changed, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(time);
        Path = path;
        _changed = changed;
        _time = time;

        // A file in a folder that does not exist is read and not watched: there is nothing a watcher could be put on.
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Folder = new WatchedFolder(directory, _ => Notify(), filter: System.IO.Path.GetFileName(path));
        }
    }

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>The folder watcher, or null when the file's folder did not exist; for a test that has to reach the watcher.</summary>
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
    public async Task<(T Result, int Attempts)> ReadAsync<T>(Func<string, T> read, Func<T, bool> succeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(succeeded);

        for (var attempt = 1; ; attempt++)
        {
            var result = read(Path);
            if (succeeded(result) || attempt >= ReadAttempts)
            {
                return (result, attempt);
            }

            await Task.Delay(BetweenReadAttempts, _time, cancellationToken).ConfigureAwait(false);
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
