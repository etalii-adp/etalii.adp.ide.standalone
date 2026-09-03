namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Runs the given cleanup exactly once when disposed - the resolver's tracking handle.</summary>
public sealed class DatabricksUnsubscriber(Action unsubscribe) : IDisposable
{
    private Action? _unsubscribe = unsubscribe;

    public void Dispose()
    {
        Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
