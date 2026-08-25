using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Diagram;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>One authenticated connection with its context stream open.</summary>
internal sealed class ProblemsFlowSession : IDisposable
{
    public required GrpcChannel Channel { get; init; }
    public required Metadata Headers { get; init; }
    public required ShortGuid ProjectId { get; init; }
    public required ShortGuid WatchId { get; init; }
    public required AsyncServerStreamingCall<ContextMessage> Call { get; init; }

    public IAsyncStreamReader<ContextMessage> Stream => Call.ResponseStream;

    public void Dispose()
    {
        Call.Dispose();
        Channel.Dispose();
    }
}
