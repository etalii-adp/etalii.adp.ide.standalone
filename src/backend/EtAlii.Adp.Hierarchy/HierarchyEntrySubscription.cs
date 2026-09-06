namespace EtAlii.Adp.Hierarchy;

internal sealed class HierarchyEntrySubscription : IDisposable
{
    private Action? _dispose;

    public HierarchyEntrySubscription(Action dispose)
    {
        _dispose = dispose;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
