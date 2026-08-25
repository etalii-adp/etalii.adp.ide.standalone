namespace EtAlii.Adp.Backend.Tests;

/// <summary>A store whose <c>Changed</c> the test raises directly; the broadcaster never calls the rest.</summary>
internal sealed class HistoryActionsBroadcasterFakeStore : IHistoryStackStore
{
    public event EventHandler<HistoryChangedEventArgs>? Changed;

    public void Raise(string rootPath) => Changed?.Invoke(this, new HistoryChangedEventArgs(rootPath));

    public IHistoryStack Get(string rootPath) => throw new NotSupportedException();

    public void Retain(string rootPath) => throw new NotSupportedException();

    public void Release(string rootPath) => throw new NotSupportedException();
}
