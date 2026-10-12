using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>One signed-in connection to a project, with its context stream open.</summary>
internal sealed class ProblemsFlowSession(FlowSession flow, AsyncServerStreamingCall<ContextMessage> call) : IDisposable
{
    public FlowSession Flow => flow;

    public IAsyncStreamReader<ContextMessage> Stream => call.ResponseStream;

    public void Dispose()
    {
        call.Dispose();
        flow.Dispose();
    }
}
