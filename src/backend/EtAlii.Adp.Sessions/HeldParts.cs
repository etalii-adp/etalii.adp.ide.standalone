using System.Runtime.ExceptionServices;
using Serilog;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// What a scope acquired, in the order it was acquired, released in the reverse order and once.
/// An acquisition that fails releases what was acquired before it, so a scope is either held whole
/// or not at all: the leak of a watch whose first discovery failed after its history was retained
/// cannot be written against it (code-structure FR-023, FR-067).
/// </summary>
internal sealed class HeldParts : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<HeldParts>();

    private readonly Lock _gate = new();
    private List<IDisposable>? _held;

    private HeldParts(List<IDisposable> held) => _held = held;

    /// <summary>Runs each acquisition in turn; when one throws, releases those before it and rethrows.</summary>
    public static HeldParts Acquire(IEnumerable<Func<IDisposable>> acquisitions)
    {
        var held = new List<IDisposable>();
        try
        {
            foreach (var acquire in acquisitions)
            {
                held.Add(acquire());
            }
        }
        catch
        {
            LogFailures(ReleaseAll(held));
            throw;
        }

        return new HeldParts(held);
    }

    /// <summary>
    /// Acquires one more part, released with the rest and before them. Once the parts are released,
    /// what is acquired is released at once and <see cref="ObjectDisposedException"/> is thrown, so a
    /// part acquired by a connection that is already going is not held by nobody.
    /// </summary>
    public T Hold<T>(Func<T> acquire)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(acquire);

        // Acquired outside the gate, so a slow acquisition holds up no release; added under it, so
        // a release that ran meanwhile is seen.
        var part = acquire();
        lock (_gate)
        {
            if (_held is not null)
            {
                _held.Add(part);
                return part;
            }
        }

        LogFailures(ReleaseAll([part]));
        throw new ObjectDisposedException(nameof(ConnectionSession), "The scope was released before this part could join it.");
    }

    /// <summary>
    /// Releases every part, the last acquired first, even when one of them throws; then throws what
    /// was thrown, alone or aggregated. A second call releases nothing.
    /// </summary>
    public void Dispose()
    {
        List<IDisposable>? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
        }

        if (held is null)
        {
            return;
        }

        var failures = ReleaseAll(held);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static List<Exception> ReleaseAll(IReadOnlyList<IDisposable> held)
    {
        var failures = new List<Exception>();
        for (var index = held.Count - 1; index >= 0; index--)
        {
            try
            {
                held[index].Dispose();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        return failures;
    }

    private static void LogFailures(List<Exception> failures)
    {
        foreach (var failure in failures)
        {
            // The exception the caller sees is the one that matters; a release failing behind it
            // is logged rather than allowed to hide it.
            _logger.Warning(failure, "Releasing a part of a scope that could not be held threw as well");
        }
    }
}
