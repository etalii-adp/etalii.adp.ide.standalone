using System.Collections.Concurrent;
using Serilog;

namespace EtAlii.Adp;

/// <summary>
/// The work an owner starts and does not await where it starts it: each task is recorded while it
/// runs and forgotten when it returns, its fault is logged when nothing else caught it, and the
/// owner stops it and waits for it when it closes, so no task is started and left
/// (code-structure FR-070).
/// </summary>
/// <remarks>
/// A started task runs on the caller's thread up to its first wait. Work started after
/// <see cref="StopAsync"/> or <see cref="Dispose"/> is handed a token that is already cancelled.
/// </remarks>
public sealed class RunningTasks : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<RunningTasks>();

    private readonly CancellationTokenSource _stopping = new();

    // Read once, here: the token outlives the source's disposal, which its Token property does not.
    private readonly CancellationToken _stoppingToken;

    // Keyed by a token each task knows and its task does not, so it can remove itself; null for
    // the moment between a task starting and its task being known.
    private readonly ConcurrentDictionary<object, Task?> _tasks = new();
    private int _disposed;

    public RunningTasks() => _stoppingToken = _stopping.Token;

    /// <summary>The number of tasks still running.</summary>
    public int Count => _tasks.Count;

    /// <summary>Starts <paramref name="work"/> with a token that is cancelled when this stops.</summary>
    public void Start(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var key = new object();
        _tasks[key] = null;
        var task = RunAsync(work, key);
        _tasks.TryUpdate(key, task, null);
    }

    /// <summary>Cancels the tasks still running and waits until they have returned.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            await _stopping.CancelAsync();
        }

        await Task.WhenAll(_tasks.Values.OfType<Task>()).WaitAsync(cancellationToken);
    }

    /// <summary>Cancels the tasks still running without waiting for them. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _stopping.Cancel();
        _stopping.Dispose();
    }

    /// <summary>Never throws: a task that fails is logged here, and one that is stopped simply ends.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> work, object key)
    {
        try
        {
            await work(_stoppingToken);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Stopped; nothing is owed.
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "A task started and left running failed");
        }
        finally
        {
            _tasks.TryRemove(key, out _);
        }
    }
}
