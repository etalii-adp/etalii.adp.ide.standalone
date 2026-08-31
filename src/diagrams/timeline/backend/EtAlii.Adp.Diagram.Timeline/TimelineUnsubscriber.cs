namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Runs its action once, when the tracking it stands for is disposed.</summary>
internal sealed class TimelineUnsubscriber(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
