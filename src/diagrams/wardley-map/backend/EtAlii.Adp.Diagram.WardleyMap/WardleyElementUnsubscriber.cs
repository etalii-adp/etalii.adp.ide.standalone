namespace EtAlii.Adp.Diagram.WardleyMap;

internal sealed class WardleyElementUnsubscriber(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
