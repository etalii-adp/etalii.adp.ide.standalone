namespace EtAlii.Adp.Backend.Tests;

internal sealed class StubResolverSubscription(ContextSelectionStoreStubResolver owner) : IDisposable
{
    public void Dispose() => owner.Disposed++;
}
