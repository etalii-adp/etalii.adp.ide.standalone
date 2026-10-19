using EtAlii.Adp.History;

namespace EtAlii.Adp.Context.Tests;

/// <summary>A store whose <c>Changed</c> the test raises directly; the broadcaster never calls the rest.</summary>
internal sealed class HistoryActionsBroadcasterFakeStore : IHistoryStackStore
{
    public event EventHandler<HistoryChangedEventArgs>? Changed;

    public void Raise(string rootPath) => Changed?.Invoke(this, new HistoryChangedEventArgs(ProjectRoot.From(rootPath)));

    public IHistoryStack Get(ProjectRoot root) => throw new NotSupportedException();

    public void Retain(ProjectRoot root) => throw new NotSupportedException();

    public void Release(ProjectRoot root) => throw new NotSupportedException();
}
