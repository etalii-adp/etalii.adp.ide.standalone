namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Runs its action once, when the tracking it stands for is disposed.</summary>
internal sealed class DependencyGraphUnsubscriber(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
