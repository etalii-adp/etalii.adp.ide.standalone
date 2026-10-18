using System.Threading.Channels;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Serilog;
using Proto = EtAlii.Adp.Designer.Wire;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The designer family's calls (knowledge-designer Requirements 10.2 and 10.7): a table joins
/// the tab's one stream, leaves it, is told which lines are in sight and which view is shown,
/// and takes the author's gestures. It names no designer type; the module's session does the
/// work, and this carries its table model to the connection.
/// </summary>
/// <remarks>
/// Served here, beside <see cref="WorkspaceService"/>, for the reason the editor family's call
/// is: this is the project that holds the tab's stream and its connections. Every call is
/// unary - a table's content is the <c>table</c> member of <c>WorkspaceService.Watch</c>, and a
/// streaming call of this service's own would be the second stream the workspace contract
/// forbids.
/// </remarks>
public sealed class DesignerService : Proto.DesignerService.DesignerServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<DesignerService>();

    private readonly IProjectStore _projectStore;
    private readonly DesignerFileRouter _router;
    private readonly DesignerSessionFactories _factories;
    private readonly WorkspaceConnections _connections;
    private readonly TableStreams _streams;
    private readonly StreamMap<IDesignerSession> _tableStream;

    public DesignerService(
        IProjectStore projectStore,
        DesignerFileRouter router,
        DesignerSessionFactories factories,
        WorkspaceConnections connections,
        TableStreams streams)
    {
        _projectStore = projectStore;
        _router = router;
        _factories = factories;
        _connections = connections;
        _streams = streams;
        _tableStream = new StreamMap<IDesignerSession>
        {
            Kind = "Table",
            RunAsync = RunAsync,
            Ended = streamId => new WorkspaceMessage { Table = new Proto.TableStreamMessage { StreamId = streamId, Ended = new Proto.TableStreamEnded() } },
        };
    }

    public override async Task<Proto.OpenTableResponse> OpenTable(Proto.OpenTableRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var connection = _connections.Require((ShortGuid)request.WatchId, userId);

        var session = OpenSession(userId, connection.WatchId, request);

        await connection.OpenStreamAsync((ShortGuid)request.StreamId, session, _tableStream);
        return new Proto.OpenTableResponse();
    }

    public override Task<Proto.CloseTableResponse> CloseTable(Proto.CloseTableRequest request, ServerCallContext context)
    {
        var connection = _connections.Find((ShortGuid)request.WatchId, SessionContext.GetUserId(context));
        if (connection is not null && connection.Stop((ShortGuid)request.StreamId))
        {
            _logger.Debug("Closed table stream {StreamId} on watch {WatchId}", (ShortGuid)request.StreamId, (ShortGuid)request.WatchId);
        }

        return Task.FromResult(new Proto.CloseTableResponse());
    }

    public override Task<Proto.SetTableWindowResponse> SetTableWindow(Proto.SetTableWindowRequest request, ServerCallContext context)
    {
        SessionOf(context, request.WatchId, request.StreamId).SetWindow(Math.Max(0, request.First), Math.Max(0, request.Count));
        return Task.FromResult(new Proto.SetTableWindowResponse());
    }

    public override Task<Proto.SetTableViewResponse> SetTableView(Proto.SetTableViewRequest request, ServerCallContext context)
    {
        SessionOf(context, request.WatchId, request.StreamId).SetActiveView(request.ViewId);
        return Task.FromResult(new Proto.SetTableViewResponse());
    }

    public override Task<Proto.TableEditResponse> Edit(Proto.TableEditRequest request, ServerCallContext context)
    {
        var session = SessionOf(context, request.WatchId, request.StreamId);
        var gesture = TableWire.FromProto(request.Gesture ?? new Proto.TableGesture());
        var error = session.Edit((ShortGuid)request.EditId, gesture);
        if (error.Length > 0)
        {
            _logger.Debug("Refused the {Gesture} gesture on table stream {StreamId}: {Reason}", gesture.Kind, (ShortGuid)request.StreamId, error);
        }

        return Task.FromResult(new Proto.TableEditResponse { Error = error });
    }

    /// <summary>
    /// The session of a running table stream of the calling user, or a transient refusal: a call
    /// for a stream that is gone is a reconnect racing a close, and the client re-opens.
    /// </summary>
    private IDesignerSession SessionOf(ServerCallContext context, Documents.Wire.ShortGuid wireWatchId, Documents.Wire.ShortGuid wireStreamId)
    {
        var watchId = (ShortGuid)wireWatchId;
        if (_connections.Find(watchId, SessionContext.GetUserId(context)) is not null && _streams.Find(watchId, (ShortGuid)wireStreamId) is { } session)
        {
            return session;
        }

        throw new RpcException(new Status(StatusCode.Unavailable, "The table is not open."));
    }

    /// <summary>
    /// Resolves what the request names to a designer's document and opens its session, or
    /// refuses with a <see cref="PermanentRefusal"/> code.
    /// </summary>
    private IDesignerSession OpenSession(ShortGuid userId, ShortGuid watchId, Proto.OpenTableRequest request)
    {
        var asked = string.Join('/', request.Path?.Segments ?? []);
        if (request.Path is null ||
            !ProjectTextFile.TryResolve(_projectStore, request.ProjectId, request.Path, userId, out var file) ||
            _router.Route(file.FullPath, file.Root.FullPath) is not DesignerRouted { BodyPath: { } bodyPath } routed ||
            !File.Exists(bodyPath))
        {
            // The client is told only that it failed; the log keeps what it asked for.
            _logger.Warning("Refusing to open {Path} as a table on watch {WatchId}: it is not a designer's document with a body", asked, watchId);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The document cannot be opened."));
        }

        var factory = _factories.Find(routed.Definition.Origin);
        if (factory is null)
        {
            _logger.Warning("Refusing to open {Path} on watch {WatchId}: no session factory is deployed for {Origin}", asked, watchId, routed.Definition.Origin);
            throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"'{routed.Definition.Origin}' documents cannot be opened yet."));
        }

        _logger.Debug("Routed {Path} to {Origin}, body {BodyPath}, on watch {WatchId}", asked, routed.Definition.Origin, bodyPath, watchId);
        return factory.Open(watchId, file.Root.FullPath, routed.RegistrationPath, bodyPath);
    }

    /// <summary>
    /// Writes the session's baseline, then every change it raises, onto the connection's stream
    /// under <paramref name="streamId"/>, until the stream is closed or the connection ends.
    /// Disposes the session when it returns.
    /// </summary>
    private async Task RunAsync(IDesignerSession opened, WorkspaceConnection connection, Documents.Wire.ShortGuid streamId, CancellationToken cancellationToken)
    {
        // Before the first wait, so a call that follows the open finds the session.
        var id = (ShortGuid)streamId;
        _streams.Add(connection.WatchId, id, opened);
        var changes = Channel.CreateUnbounded<TableChange>();

        await using var session = opened;
        session.Changed += OnChanged;
        try
        {
            await WriteAsync(connection, new Proto.TableStreamMessage { StreamId = streamId, Baseline = TableWire.ToProto(session.Baseline()) }, cancellationToken);
            _logger.Information("Opened table stream {StreamId} on watch {WatchId}", id, connection.WatchId);

            await foreach (var change in changes.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteAsync(connection, new Proto.TableStreamMessage { StreamId = streamId, Change = TableWire.ToProto(change) }, cancellationToken);
            }
        }
        finally
        {
            session.Changed -= OnChanged;
            _streams.Remove(connection.WatchId, id, session);
        }

        return;

        // MUST STAY NON-BLOCKING: a session may raise this while holding its lock.
        void OnChanged(object? sender, TableChangedEventArgs args)
        {
            foreach (var change in args.Changes)
            {
                changes.Writer.TryWrite(change);
            }
        }
    }

    private static Task WriteAsync(WorkspaceConnection connection, Proto.TableStreamMessage message, CancellationToken cancellationToken) =>
        connection.Writer.WriteAsync(new WorkspaceMessage { Table = message }, cancellationToken).AsTask();
}
