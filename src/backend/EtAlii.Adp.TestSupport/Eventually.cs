namespace EtAlii.Adp.TestSupport;

/// <summary>
/// Waits until something a test cannot be told about directly has happened: a log line, a file,
/// a state another thread reaches.
/// </summary>
/// <remarks>
/// The one polling helper, so a test asks "until when" rather than sleeping a guess. The clock is a
/// <see cref="TimeProvider"/>, the system's unless a test passes a fake one, so the patience and the
/// interval between looks are both measured on the same clock the caller controls.
/// </remarks>
public static class Eventually
{
    /// <summary>How long a wait lasts when the caller names no patience.</summary>
    private static readonly TimeSpan DefaultPatience = TimeSpan.FromSeconds(10);

    /// <summary>How long between two looks when the caller names no interval.</summary>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Whether <paramref name="condition"/> came to hold before <paramref name="patience"/> ran out,
    /// asked as often as <see cref="ValueAsync{T}"/> asks its probe.
    /// </summary>
    public static async Task<bool> TrueAsync(
        Func<bool> condition,
        CancellationToken cancellationToken,
        TimeSpan? patience = null,
        TimeSpan? interval = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return await ValueAsync(() => condition() ? _held : null, cancellationToken, patience, interval, time) is not null;
    }

    private static readonly object _held = new();

    /// <summary>
    /// The first non-null value <paramref name="probe"/> returns, or null when
    /// <paramref name="patience"/> runs out first. The probe is asked once more at the deadline, so
    /// a value that arrives during the last interval is not missed.
    /// </summary>
    public static async Task<T?> ValueAsync<T>(
        Func<T?> probe,
        CancellationToken cancellationToken,
        TimeSpan? patience = null,
        TimeSpan? interval = null,
        TimeProvider? time = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(probe);
        time ??= TimeProvider.System;
        var started = time.GetTimestamp();
        var limit = patience ?? DefaultPatience;
        while (true)
        {
            var value = probe();
            if (value is not null)
            {
                return value;
            }

            if (time.GetElapsedTime(started) >= limit)
            {
                return null;
            }

            await Task.Delay(interval ?? DefaultInterval, time, cancellationToken);
        }
    }
}
