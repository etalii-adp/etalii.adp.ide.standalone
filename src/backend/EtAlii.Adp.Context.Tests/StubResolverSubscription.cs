namespace EtAlii.Adp.Context.Tests;

internal sealed class StubResolverSubscription(ContextSelectionStoreStubResolver owner) : IDisposable
{
    public void Dispose() => owner.Disposed++;
}
