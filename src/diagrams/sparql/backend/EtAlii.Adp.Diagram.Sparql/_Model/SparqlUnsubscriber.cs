namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>Undoes one subscription when the tracker is done with it.</summary>
public sealed class SparqlUnsubscriber(Action unsubscribe) : IDisposable
{
    private Action? _unsubscribe = unsubscribe;

    public void Dispose()
    {
        Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
