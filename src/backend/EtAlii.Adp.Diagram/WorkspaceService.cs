using System.Threading.Channels;
using EtAlii.Adp.Authentication;
using EtAlii.Adp.Diagram.Wire;
using Grpc.Core;
using Serilog;
using ContextService = EtAlii.Adp.Context.ContextService;
using HierarchyService = EtAlii.Adp.Hierarchy.HierarchyService;
using WatchContextRequest = EtAlii.Adp.Context.Wire.WatchContextRequest;
using WatchHierarchyRequest = EtAlii.Adp.Hierarchy.Wire.WatchHierarchyRequest;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The one long-lived stream a browser tab holds (two-tab-connection-wedge Requirement 3.1). It
/// carries what three streams used to - the hierarchy's changes, the context's messages and the
/// active document's deltas - so a tab reached over cleartext HTTP/1.1 costs one of the browser's
/// six connections per origin rather than three.
/// </summary>
/// <remarks>
/// <para>
/// It adds no behaviour of its own. The hierarchy watch, the context stream and the diagram pump are
/// the same code their own RPCs run, handed a writer onto this stream instead of their own call's.
/// </para>
/// <para>
/// A diagram stream cannot be opened by a second streaming call without defeating the purpose, and
/// grpc-web cannot send on a stream it is reading, so a diagram joins the connection through the
/// unary <see cref="OpenDiagram"/> and leaves through <see cref="CloseDiagram"/> - the same two
/// correlated one-way legs the rest of the API already uses.
/// </para>
/// </remarks>
public sealed class WorkspaceService(HierarchyService hierarchy, ContextService context, DiagramService diagrams, WorkspaceConnections connections) : Wire.WorkspaceService.WorkspaceServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<WorkspaceService>();

    private readonly HierarchyService _hierarchy = hierarchy;
    private readonly ContextService _context = context;
    private readonly DiagramService _diagrams = diagrams;
    private readonly WorkspaceConnections _connections = connections;

    public override async Task Watch(WatchWorkspaceRequest request, IServerStreamWriter<WorkspaceMessage> responseStream, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var watchId = (ShortGuid)request.WatchId;

        // One reader writes to the call; every source writes here. A call's stream accepts one
        // write at a time, and the sources run concurrently.
        var channel = Channel.CreateUnbounded<WorkspaceMessage>(new UnboundedChannelOptions { SingleReader = true });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var connection = _connections.Register(watchId, userId, channel.Writer, lifetime.Token);
        _logger.Information("Workspace stream open on watch {WatchId} for project {ProjectId}", watchId, request.ProjectId);

        var sources = new[]
        {
            _hierarchy.RunWatchAsync(
                userId,
                new WatchHierarchyRequest { ProjectId = request.ProjectId, WatchId = request.WatchId },
                (message, token) => channel.Writer.WriteAsync(new WorkspaceMessage { Hierarchy = message }, token).AsTask(),
                lifetime.Token),
            _context.RunWatchAsync(
                userId,
                new WatchContextRequest { ProjectId = request.ProjectId, WatchId = request.WatchId },
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
            _connections.Remove(connection);
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
        var opened = _diagrams.OpenSession(userId, request.Request);

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

    private async Task PumpAsync(WorkspaceConnection connection, ShortGuid streamId, DiagramService.OpenedDiagram opened, CancellationTokenSource source)
    {
        Documents.Wire.ShortGuid wireStreamId = streamId;
        try
        {
            await _diagrams.PumpAsync(
                opened,
                (delta, token) => connection.Writer.WriteAsync(
                    new WorkspaceMessage { Diagram = new DiagramStreamMessage { StreamId = wireStreamId, Delta = delta } },
                    token).AsTask(),
                source.Token);
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
