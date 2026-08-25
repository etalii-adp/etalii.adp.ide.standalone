using EtAlii.Adp.Backend.Context;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

internal sealed class StubResolverNoopDisposable : IDisposable
{
    public void Dispose()
    {
    }
}
