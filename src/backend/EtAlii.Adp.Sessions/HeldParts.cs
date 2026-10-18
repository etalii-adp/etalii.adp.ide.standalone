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

    private IReadOnlyList<IDisposable>? _held;

    private HeldParts(IReadOnlyList<IDisposable> held) => _held = held;

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
            foreach (var failure in ReleaseAll(held))
            {
                // The acquisition's own exception is the one the caller needs; a release failing
                // behind it is logged rather than allowed to hide it.
                _logger.Warning(failure, "Releasing a part of a scope whose acquisition failed threw as well");
            }

            throw;
        }

        return new HeldParts(held);
    }

    /// <summary>
    /// Releases every part, the last acquired first, even when one of them throws; then throws what
    /// was thrown, alone or aggregated. A second call releases nothing.
    /// </summary>
    public void Dispose()
    {
        var held = Interlocked.Exchange(ref _held, null);
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
}
