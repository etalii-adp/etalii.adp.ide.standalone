using System.Diagnostics.CodeAnalysis;
using Serilog;

namespace EtAlii.Adp;

/// <summary>
/// Values held per key for as long as something retains them, and dropped once nothing has
/// for a whole idle window (code-structure FR-051). A value is made on first use, by the
/// factory that use passes; each <see cref="Retain"/> holds it and each <see cref="Release"/> lets go, and the window starts
/// when the last holder lets go. A retain inside the window keeps the value, so a client that
/// reconnects within it finds what it left.
/// </summary>
/// <remarks>
/// <para>
/// Nothing holds a value that <see cref="GetOrAdd"/> made until it is retained. Whether such a
/// value is dropped when nothing claims it within the window is the owner's choice, made once
/// with <c>dropUnclaimed</c>: a hierarchy model that was listed but never watched is, a history
/// that was read but never retained is not.
/// </para>
/// <para>
/// The windows run on <see cref="Debouncer{TKey}"/>, on the clock it is given. The callback
/// that lets a value go runs outside every lock this holds, whether the value was removed,
/// timed out or disposed with the registry.
/// </para>
/// </remarks>
public sealed class RetainedRegistry<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private static readonly ILogger _logger = Log.ForContext<RetainedRegistry<TKey, TValue>>();

    private readonly TimeSpan _window;
    private readonly Action<TKey, TValue> _dropped;
    private readonly bool _dropUnclaimed;
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, Held> _held;
    private readonly Debouncer<TKey> _idle;

    /// <param name="window">How long a value nothing retains is kept; more than zero.</param>
    /// <param name="dropped">Lets a value go: removed, timed out, or disposed with the registry.</param>
    /// <param name="time">The clock whose timers measure the window.</param>
    /// <param name="dropUnclaimed">Whether a value <see cref="GetOrAdd"/> made is dropped when nothing retains it within the window.</param>
    /// <param name="comparer">How keys compare; the default comparer when <see langword="null"/>.</param>
    public RetainedRegistry(
        TimeSpan window,
        Action<TKey, TValue> dropped,
        TimeProvider time,
        bool dropUnclaimed = false,
        IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        _window = window;
        _dropped = dropped;
        _dropUnclaimed = dropUnclaimed;
        _held = new Dictionary<TKey, Held>(comparer);
        _idle = new Debouncer<TKey>(window, OnIdle, time, comparer);
    }

    /// <summary>How many values are held, retained or not.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>The value of <paramref name="key"/>, made by <paramref name="create"/> if there is none; retains nothing.</summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> create)
    {
        lock (_gate)
        {
            return HeldFor(key, create).Value;
        }
    }

    /// <summary>The value of <paramref name="key"/> if one is held; makes nothing.</summary>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(key, out var held))
            {
                value = held.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>Holds the value of <paramref name="key"/>, made by <paramref name="create"/> if there is none, and stops its window.</summary>
    public TValue Retain(TKey key, Func<TKey, TValue> create)
    {
        lock (_gate)
        {
            var held = HeldFor(key, create);
            held.Count++;
            _idle.Cancel(key);
            return held.Value;
        }
    }

    /// <summary>Holds the value of <paramref name="key"/> only if one is held already; whether it was.</summary>
    public bool TryRetain(TKey key)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(key, out var held))
            {
                return false;
            }

            held.Count++;
            _idle.Cancel(key);
            return true;
        }
    }

    /// <summary>Lets go of the value of <paramref name="key"/>; the last to let go starts its window.</summary>
    public void Release(TKey key)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(key, out var held))
            {
                return;
            }

            if (held.Count > 0)
            {
                held.Count--;
            }

            if (held.Count == 0)
            {
                _idle.Schedule(key);
            }
        }
    }

    /// <summary>Drops the value of <paramref name="key"/> now, retained or not; whether one was held.</summary>
    public bool Remove(TKey key)
    {
        Held? held;
        lock (_gate)
        {
            if (!_held.Remove(key, out held))
            {
                return false;
            }

            _idle.Cancel(key);
        }

        _dropped(key, held.Value);
        return true;
    }

    /// <summary>Every value held at this moment.</summary>
    public IReadOnlyList<TValue> Values()
    {
        lock (_gate)
        {
            return _held.Values.Select(held => held.Value).ToArray();
        }
    }

    /// <summary>Drops every value and starts no window more.</summary>
    public void Dispose()
    {
        KeyValuePair<TKey, Held>[] all;
        lock (_gate)
        {
            _idle.Dispose();
            all = _held.ToArray();
            _held.Clear();
        }

        foreach ((TKey key, Held held) in all)
        {
            _dropped(key, held.Value);
        }
    }

    private Held HeldFor(TKey key, Func<TKey, TValue> create)
    {
        if (_held.TryGetValue(key, out var held))
        {
            return held;
        }

        held = new Held(create(key));
        _held.Add(key, held);
        if (_dropUnclaimed)
        {
            _idle.Schedule(key);
        }

        return held;
    }

    private void OnIdle(TKey key)
    {
        Held? held;
        int remaining;
        lock (_gate)
        {
            if (!_held.TryGetValue(key, out held) || held.Count > 0)
            {
                return; // Retained again since the window started.
            }

            _held.Remove(key);
            remaining = _held.Count;
        }

        _logger.Debug(
            "Dropped the {Value} for {Key}: nothing retained it for {Window}; {Remaining} still held",
            typeof(TValue).Name,
            key,
            _window,
            remaining);
        _dropped(key, held.Value);
    }

    private sealed class Held(TValue value)
    {
        public TValue Value { get; } = value;

        public int Count { get; set; }
    }
}
