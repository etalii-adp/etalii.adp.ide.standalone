using Grpc.Core;
using Grpc.Net.Client;

// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

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
