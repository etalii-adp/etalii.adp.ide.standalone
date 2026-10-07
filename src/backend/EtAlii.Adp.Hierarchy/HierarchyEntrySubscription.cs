namespace EtAlii.Adp.Hierarchy;

internal sealed class HierarchyEntrySubscription(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
