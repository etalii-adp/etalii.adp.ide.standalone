using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.C4;

internal sealed class C4ElementUnsubscriber(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
