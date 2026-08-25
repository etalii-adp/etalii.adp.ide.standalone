namespace EtAlii.Adp.Diagram.Mindmap;

internal sealed class MindmapNodeSubscription(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
