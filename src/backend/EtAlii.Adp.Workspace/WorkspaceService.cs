using System.Threading.Channels;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Serilog;
using ContextWatch = EtAlii.Adp.Context.ContextWatch;
using HierarchyWatch = EtAlii.Adp.Hierarchy.HierarchyWatch;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The one long-lived stream a browser tab holds (two-tab-connection-wedge Requirement 3.1). It
/// carries what three streams used to - the hierarchy's changes, the context's messages and the
/// active document's deltas - so a tab reached over cleartext HTTP/1.1 costs one of the browser's
/// six connections per origin rather than three.
/// </summary>
/// <remarks>
/// <para>
/// It adds no behaviour of its own. The hierarchy watch, the context stream and the diagram pump are
/// the domain services their own RPCs run too (<see cref="HierarchyWatch"/>, <see cref="ContextWatch"/>,
/// <see cref="DiagramPump"/>), handed a writer onto this stream instead of their own call's; no gRPC
/// service calls another (code-structure FR-067).
/// </para>
/// <para>
/// A diagram stream cannot be opened by a second streaming call without defeating the purpose, and
/// grpc-web cannot send on a stream it is reading, so a diagram joins the connection through the
/// unary <see cref="OpenDiagram"/> and leaves through <see cref="CloseDiagram"/> - the same two
/// correlated one-way legs the rest of the API already uses.
/// </para>
/// </remarks>
public sealed class WorkspaceService : Diagram.Wire.WorkspaceService.WorkspaceServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<WorkspaceService>();

    private readonly IProjectStore _projectStore;
    private readonly ConnectionSessions _sessions;
    private readonly HierarchyWatch _hierarchy;
    private readonly ContextWatch _context;
    private readonly DiagramPump _diagrams;
    private readonly WorkspaceConnections _connections;

    public WorkspaceService(
        IProjectStore projectStore,
        ConnectionSessions sessions,
        HierarchyWatch hierarchy,
        ContextWatch context,
        DiagramPump diagrams,
        WorkspaceConnections connections)
    {
        _projectStore = projectStore;
        _sessions = sessions;
        _hierarchy = hierarchy;
        _context = context;
        _diagrams = diagrams;
        _connections = connections;
    }

    public override async Task Watch(WatchWorkspaceRequest request, IServerStreamWriter<WorkspaceMessage> responseStream, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var watchId = (ShortGuid)request.WatchId;
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var root, out var error))
        {
            _logger.Warning("Refused a workspace stream for {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, error));
        }

        // Everything the connection holds - its project's history and problems, its hierarchy
        // model, its registrations, its place among the open connections - is acquired through this
        // one scope and released when it goes (code-structure FR-067).
        using var session = _sessions.Open(watchId, root);

        // One reader writes to the call; every source writes here. A call's stream accepts one
        // write at a time, and the sources run concurrently.
        var channel = Channel.CreateUnbounded<WorkspaceMessage>(new UnboundedChannelOptions { SingleReader = true });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        // Read here rather than in the acquisition, which captures nothing the method disposes.
        var ended = lifetime.Token;
        session.Hold(() => _connections.Register(watchId, userId, channel.Writer, ended));
        _logger.Information("Workspace stream open on watch {WatchId} for project {ProjectId}", watchId, request.ProjectId);

        var sources = new[]
        {
            _hierarchy.RunAsync(
                session,
                (message, token) => channel.Writer.WriteAsync(new WorkspaceMessage { Hierarchy = message }, token).AsTask(),
                lifetime.Token),
            _context.RunAsync(
                session,
                (message, token) => channel.Writer.WriteAsync(new WorkspaceMessage { Context = message }, token).AsTask(),
                lifetime.Token),
        };
        var drain = DrainAsync(channel.Reader, responseStream, lifetime.Token);

        try
        {
            // Every source runs until the call ends, so the first to return - a refusal, a fault,
            // or the client going away - ends them all.
            await Task.WhenAny(sources.Append(drain));
        }
        finally
        {
            await lifetime.CancelAsync();
            channel.Writer.TryComplete();
            try
            {
                await Task.WhenAll(sources.Append(drain));
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // The drain's own read ending with the call - not a fault.
            }

            _logger.Information("Workspace stream closed on watch {WatchId}", watchId);
        }
    }

    public override async Task<OpenDiagramStreamResponse> OpenDiagram(OpenDiagramStreamRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var watchId = (ShortGuid)request.Request.WatchId;
        var streamId = (ShortGuid)request.StreamId;

        // A connection that is not open yet is transient: the client waits for the stream's
        // baseline before it opens anything, so this is a reconnect racing a close.
        var connection = _connections.Find(watchId);
        if (connection is null || connection.UserId != userId)
        {
            _logger.Debug("Refused a diagram stream on watch {WatchId}: no workspace stream of this user is open under it", watchId);
            throw new RpcException(new Status(StatusCode.Unavailable, "The connection is not open."));
        }

        // Resolution refuses with the same permanent codes DiagramService.Open answers, so the
        // client still tells a diagram that cannot open from a connection that dropped.
        var opened = _diagrams.Open(userId, request.Request);

        var source = connection.TryStart(streamId);
        if (source is null)
        {
            await opened.Session.DisposeAsync();
            throw new RpcException(new Status(StatusCode.AlreadyExists, "A diagram stream with this id is already open."));
        }

        _ = PumpAsync(connection, streamId, opened, source);
        return new OpenDiagramStreamResponse();
    }

    public override Task<CloseDiagramStreamResponse> CloseDiagram(CloseDiagramStreamRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var connection = _connections.Find((ShortGuid)request.WatchId);
        if (connection is not null && connection.UserId == userId && connection.Stop((ShortGuid)request.StreamId))
        {
            _logger.Debug("Closed diagram stream {StreamId} on watch {WatchId}", (ShortGuid)request.StreamId, (ShortGuid)request.WatchId);
        }

        return Task.FromResult(new CloseDiagramStreamResponse());
    }

    private async Task PumpAsync(WorkspaceConnection connection, ShortGuid streamId, OpenedDiagram opened, CancellationTokenSource source)
    {
        Documents.Wire.ShortGuid wireStreamId = streamId;
        try
        {
            await _diagrams.RunAsync(
                opened,
                (delta, token) => connection.Writer.WriteAsync(
                    new WorkspaceMessage { Diagram = new DiagramStreamMessage { StreamId = wireStreamId, Delta = delta } },
                    token).AsTask(),
                source.Token);
        }
        catch (RpcException refusal) when (PermanentRefusal.IsPermanent(refusal.StatusCode))
        {
            // Refused for good after the open was answered - a baseline that could not be made.
            // Ending the stream would read as a drop the client re-opens every RECONNECT_DELAY_MS
            // for ever; the refusal is what its own Open call would have ended with.
            _logger.Debug("Diagram stream {StreamId} on watch {WatchId} refused: {Reason}", streamId, connection.WatchId, refusal.Status.Detail);
            connection.Writer.TryWrite(new WorkspaceMessage
            {
                Diagram = new DiagramStreamMessage
                {
                    StreamId = wireStreamId,
                    Refused = new DiagramStreamRefused { Code = (int)refusal.StatusCode, Message = refusal.Status.Detail },
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !source.IsCancellationRequested)
        {
            // The session failed on its own. Its own Open call would have ended in an error the
            // client re-opens from; here the connection carries on, so the stream says it ended.
            _logger.Warning(exception, "Diagram stream {StreamId} on watch {WatchId} failed; telling the client it ended", streamId, connection.WatchId);
            connection.Writer.TryWrite(new WorkspaceMessage { Diagram = new DiagramStreamMessage { StreamId = wireStreamId, Ended = new DiagramStreamEnded() } });
        }
        finally
        {
            connection.Forget(streamId, source);
            source.Dispose();
        }
    }

    private static async Task DrainAsync(ChannelReader<WorkspaceMessage> reader, IServerStreamWriter<WorkspaceMessage> responseStream, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in reader.ReadAllAsync(cancellationToken))
            {
                await responseStream.WriteAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected: the client closed the stream.
        }
    }
}
