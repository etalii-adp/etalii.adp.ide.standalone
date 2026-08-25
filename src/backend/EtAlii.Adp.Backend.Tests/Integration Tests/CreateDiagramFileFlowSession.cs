using System.Text;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

internal sealed record CreateDiagramFileFlowSession(
    GrpcChannel Channel,
    Metadata Headers,
    Contracts.ShortGuid ProjectId,
    Contracts.ShortGuid WatchId,
    HierarchyService.HierarchyServiceClient Hierarchy,
    ContextService.ContextServiceClient Context) : IDisposable
{
    public void Dispose() => Channel.Dispose();
}
