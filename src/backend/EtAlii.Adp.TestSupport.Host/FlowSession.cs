using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using Grpc.Net.Client;

namespace EtAlii.Adp.TestSupport.Host;

/// <summary>
/// One connection to a <see cref="HostFixture"/>: signed in as the developer, with a project
/// added and a watch id of its own, and the clients a flow test calls.
/// </summary>
public sealed class FlowSession : IDisposable
{
    private readonly GrpcChannel _channel;

    private FlowSession(GrpcChannel channel, Metadata headers, ShortGuid projectId)
    {
        _channel = channel;
        Headers = headers;
        ProjectId = projectId;
        Hierarchy = new HierarchyService.HierarchyServiceClient(channel);
        Context = new ContextService.ContextServiceClient(channel);
    }

    /// <summary>The signed-in session's token, as every call sends it.</summary>
    public Metadata Headers { get; }

    public ShortGuid ProjectId { get; }

    /// <summary>This connection's watch id; a test that needs a second one makes it.</summary>
    public ShortGuid WatchId { get; } = ShortGuid.NewShortGuid();

    public HierarchyService.HierarchyServiceClient Hierarchy { get; }

    public ContextService.ContextServiceClient Context { get; }

    /// <summary>Signs in on a new channel to <paramref name="host"/> and adds <paramref name="projectFolder"/>.</summary>
    public static async Task<FlowSession> OpenAsync(HostFixture host, string projectFolder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        var channel = host.CreateChannel();
        var headers = await channel.SignInAsync(cancellationToken);
        var projectId = await channel.AddProjectAsync(headers, projectFolder, cancellationToken);
        return new FlowSession(channel, headers, projectId);
    }

    /// <summary>
    /// A token that cancels after <see cref="FlowConstants.MessageTimeout"/>, for reading a stream,
    /// or as soon as <paramref name="testCancellation"/> does, so a stream that never delivers gives
    /// up when the test is cancelled rather than waiting the timeout out.
    /// </summary>
    public static CancellationTokenSource CreateMessageTimeout(CancellationToken testCancellation)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        timeout.CancelAfter(FlowConstants.MessageTimeout);
        return timeout;
    }

    public void Dispose() => _channel.Dispose();
}
