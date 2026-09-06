using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using Grpc.Net.Client;

// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

internal sealed record CreateDiagramFileFlowSession(
    GrpcChannel Channel,
    Metadata Headers,
    Common.Wire.ShortGuid ProjectId,
    Common.Wire.ShortGuid WatchId,
    HierarchyService.HierarchyServiceClient Hierarchy,
    ContextService.ContextServiceClient Context) : IDisposable
{
    public void Dispose() => Channel.Dispose();
}
