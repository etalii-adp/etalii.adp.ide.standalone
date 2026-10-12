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
    private FlowSession(GrpcChannel channel, Metadata headers, ShortGuid projectId)
    {
        Channel = channel;
        Headers = headers;
        ProjectId = projectId;
        Hierarchy = new HierarchyService.HierarchyServiceClient(channel);
        Context = new ContextService.ContextServiceClient(channel);
    }

    /// <summary>The channel to the host, for the clients of a service this session does not hold.</summary>
    public GrpcChannel Channel { get; }

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
    /// Signs in on a new channel to <paramref name="host"/> for the project <paramref name="joined"/>
    /// already added: a second connection to the same project, with a watch id of its own.
    /// </summary>
    public static async Task<FlowSession> JoinAsync(HostFixture host, FlowSession joined, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(joined);
        var channel = host.CreateChannel();
        var headers = await channel.SignInAsync(cancellationToken);
        return new FlowSession(channel, headers, joined.ProjectId);
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

    public void Dispose() => Channel.Dispose();
}
