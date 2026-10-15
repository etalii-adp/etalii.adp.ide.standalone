namespace EtAlii.Adp;

/// <summary>
/// Coalesces events per key on a clock: every <see cref="Schedule"/> starts the key's window
/// over, and once a window passes with nothing more scheduled for its key, the callback runs
/// once for it (code-structure FR-051).
/// </summary>
/// <remarks>
/// <para>
/// The callback runs on the clock's timer, outside every lock this holds, so it may take its
/// caller's own locks and may schedule again. A timer that wakes after its key was scheduled
/// again, cancelled or disposed does nothing: each scheduling carries a token of its own, and
/// only the timer holding the key's current token runs the callback.
/// </para>
/// <para>
/// What cannot be stopped is a callback already running when <see cref="Cancel"/> or
/// <see cref="Dispose"/> is called; a caller with work owed past its own disposal keeps that
/// debt itself, as the problem store does.
/// </para>
/// </remarks>
public sealed class Debouncer<TKey> : IDisposable
    where TKey : notnull
{
    private readonly TimeSpan _window;
    private readonly Action<TKey> _elapsed;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, Pending> _pending;
    private bool _disposed;

    /// <param name="window">How long a key stays quiet before its callback runs; more than zero.</param>
    /// <param name="elapsed">What runs, once per settled key.</param>
    /// <param name="time">The clock whose timers measure the window.</param>
    /// <param name="comparer">How keys compare; the default comparer when <see langword="null"/>.</param>
    public Debouncer(TimeSpan window, Action<TKey> elapsed, TimeProvider time, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(elapsed);
        ArgumentNullException.ThrowIfNull(time);
        _window = window;
        _elapsed = elapsed;
        _time = time;
        _pending = new Dictionary<TKey, Pending>(comparer);
    }

    /// <summary>Starts <paramref name="key"/>'s window over; does nothing once disposed.</summary>
    public void Schedule(TKey key)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.Remove(key, out var previous))
            {
                previous.Timer.Dispose();
            }

            var token = new object();
            var timer = _time.CreateTimer(_ => Elapse(key, token), null, _window, Timeout.InfiniteTimeSpan);
            _pending[key] = new Pending(token, timer);
        }
    }

    /// <summary>Drops <paramref name="key"/>'s pending callback; whether there was one.</summary>
    public bool Cancel(TKey key)
    {
        lock (_gate)
        {
            if (!_pending.Remove(key, out var pending))
            {
                return false;
            }

            pending.Timer.Dispose();
            return true;
        }
    }

    /// <summary>Drops every pending callback and schedules nothing more.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var pending in _pending.Values)
            {
                pending.Timer.Dispose();
            }

            _pending.Clear();
        }
    }

    private void Elapse(TKey key, object token)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var pending) || !ReferenceEquals(pending.Token, token))
            {
                return; // Scheduled again, cancelled or disposed since this timer was made.
            }

            _pending.Remove(key);
            pending.Timer.Dispose();
        }

        _elapsed(key);
    }

    private readonly record struct Pending(object Token, ITimer Timer);
}
