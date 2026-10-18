namespace EtAlii.Adp.Files;

/// <summary>
/// One turn per destination, taken by a save or a delete of that path, waited for asynchronously
/// and counted by its takers, so a turn nobody holds or waits for is dropped rather than kept for
/// the life of the process (code-structure FR-068). Keyed by the normalised full path, ignoring case
/// on Windows, where two spellings name one file and would otherwise take two turns and race;
/// case-sensitive elsewhere, where they are two files.
/// </summary>
internal sealed class PathTurns
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Turn> _turns =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>How many destinations have a turn held or waited for; zero when no save or delete is in flight.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _turns.Count;
            }
        }
    }

    /// <summary>
    /// Takes the turn of <paramref name="destination"/>, waiting for it without blocking a thread.
    /// <paramref name="waiting"/> is called first when the turn is held by another, so a wait is
    /// said only when one really happens. Disposing what is returned gives the turn back.
    /// </summary>
    public async Task<IDisposable> TakeAsync(string destination, Action waiting, CancellationToken cancellationToken)
    {
        var turn = Join(destination);
        try
        {
            // Tried first without waiting, which completes at once either way.
            if (!await turn.Semaphore.WaitAsync(0, CancellationToken.None))
            {
                waiting();
                await turn.Semaphore.WaitAsync(cancellationToken);
            }
        }
        catch
        {
            Leave(destination, turn);
            throw;
        }

        return new Taken(this, destination, turn);
    }

    private Turn Join(string destination)
    {
        lock (_gate)
        {
            if (!_turns.TryGetValue(destination, out var turn))
            {
                turn = new Turn();
                _turns.Add(destination, turn);
            }

            turn.Takers++;
            return turn;
        }
    }

    private void Leave(string destination, Turn turn)
    {
        lock (_gate)
        {
            // Every taker and waiter counts itself in and out under the gate, so the last to leave
            // knows nobody else holds or is about to wait on this turn.
            if (--turn.Takers == 0)
            {
                _turns.Remove(destination);
                turn.Semaphore.Dispose();
            }
        }
    }

    private sealed class Turn
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Takers { get; set; }
    }

    private sealed class Taken(PathTurns turns, string destination, Turn turn) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 1)
            {
                return;
            }

            turn.Semaphore.Release();
            turns.Leave(destination, turn);
        }
    }
}
