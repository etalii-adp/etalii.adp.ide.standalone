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
public sealed partial class DesignerService : Proto.DesignerService.DesignerServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<DesignerService>();

    private readonly IProjectStore _projectStore;
    private readonly DesignerFileRouter _router;
    private readonly DesignerSessionFactories _factories;
    private readonly WorkspaceConnections _connections;
    private readonly TableStreams _streams;
    private readonly StreamMap<ITableDesignerSession> _tableStream;

    public DesignerService(
        IProjectStore projectStore,
        DesignerFileRouter router,
        DesignerSessionFactories factories,
        WorkspaceConnections connections,
        TableStreams streams,
        HierarchySurfaceStreams surfaces)
    {
        _projectStore = projectStore;
        _router = router;
        _factories = factories;
        _connections = connections;
        _streams = streams;
        _surfaces = surfaces;
        _surfaceStream = new StreamMap<IHierarchySurfaceSession>
        {
            Kind = "HierarchySurface",
            RunAsync = RunSurfaceAsync,
            Ended = streamId => new WorkspaceMessage { HierarchySurface = new Proto.HierarchySurfaceStreamMessage { StreamId = streamId, Ended = new Proto.HierarchySurfaceStreamEnded() } },
        };
        _tableStream = new StreamMap<ITableDesignerSession>
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

        var session = await OpenSessionAsync<ITableDesignerSession>(userId, connection.WatchId, request.ProjectId, request.Path, "table");

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
    private ITableDesignerSession SessionOf(ServerCallContext context, Documents.Wire.ShortGuid wireWatchId, Documents.Wire.ShortGuid wireStreamId)
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
    /// refuses with a <see cref="PermanentRefusal"/> code - also when the document's surface is
    /// not the one asked for, a hierarchy surface opened as a table say.
    /// </summary>
    private async Task<TSession> OpenSessionAsync<TSession>(ShortGuid userId, ShortGuid watchId, Documents.Wire.ShortGuid projectId, Documents.Wire.Path? path, string surface)
        where TSession : class, IDesignerSession
    {
        var asked = string.Join('/', path?.Segments ?? []);
        if (path is null ||
            !ProjectTextFile.TryResolve(_projectStore, projectId, path, userId, out var file) ||
            // A body is its type's by its envelope first, with or without a registration (spec 029 R3, R4), as ToolRouter routes it.
            (_router.RouteByEnvelope(file.FullPath) ?? _router.Route(file.FullPath, file.Root.FullPath)) is not DesignerRouted { BodyPath: { } bodyPath } routed ||
            !File.Exists(bodyPath))
        {
            // The client is told only that it failed; the log keeps what it asked for.
            _logger.Warning("Refusing to open {Path} as a {Surface} on watch {WatchId}: it is not a designer's document with a body", asked, surface, watchId);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The document cannot be opened."));
        }

        var factory = _factories.Find(routed.Definition.Origin);
        if (factory is null)
        {
            _logger.Warning("Refusing to open {Path} on watch {WatchId}: no session factory is deployed for {Origin}", asked, watchId, routed.Definition.Origin);
            throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"'{routed.Definition.Origin}' documents cannot be opened yet."));
        }

        _logger.Debug("Routed {Path} to {Origin}, body {BodyPath}, on watch {WatchId}", asked, routed.Definition.Origin, bodyPath, watchId);
        var opened = factory.Open(watchId, file.Root, routed.RegistrationPath, bodyPath);
        if (opened is TSession session)
        {
            return session;
        }

        await opened.DisposeAsync();
        _logger.Warning("Refusing to open {Path} as a {Surface} on watch {WatchId}: {Origin} documents are drawn by another surface", asked, surface, watchId, routed.Definition.Origin);
        throw new RpcException(new Status(PermanentRefusal.CannotOpen, $"'{routed.Definition.Origin}' documents are not a {surface}."));
    }

    /// <summary>
    /// Writes the session's baseline, then every change it raises, onto the connection's stream
    /// under <paramref name="streamId"/>, until the stream is closed or the connection ends.
    /// Disposes the session when it returns.
    /// </summary>
    private async Task RunAsync(ITableDesignerSession opened, WorkspaceConnection connection, Documents.Wire.ShortGuid streamId, CancellationToken cancellationToken)
    {
        // Before the first wait, so a call that follows the open finds the session.
        var id = (ShortGuid)streamId;
        _streams.Add(connection.WatchId, id, opened);
        // Bounded by what one event raised, as a diagram stream's deltas are; a tab that stops
        // reading ends the stream, and its consumer re-opens it for a fresh baseline (B-28).
        var changes = StreamBound.Ending<TableChange[]>($"Table stream {id} on watch {connection.WatchId}");

        await using var session = opened;
        session.Changed += OnChanged;
        try
        {
            await WriteAsync(connection, new Proto.TableStreamMessage { StreamId = streamId, Baseline = TableWire.ToProto(session.Baseline()) }, cancellationToken);
            _logger.Information("Opened table stream {StreamId} on watch {WatchId}", id, connection.WatchId);

            await foreach (var batch in changes.Reader.ReadAllAsync(cancellationToken))
            {
                foreach (var change in batch)
                {
                    await WriteAsync(connection, new Proto.TableStreamMessage { StreamId = streamId, Change = TableWire.ToProto(change) }, cancellationToken);
                }
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
            if (args.Changes.Count > 0)
            {
                changes.Writer.TryWrite([.. args.Changes]);
            }
        }
    }

    private static Task WriteAsync(WorkspaceConnection connection, Proto.TableStreamMessage message, CancellationToken cancellationToken) =>
        connection.Writer.WriteAsync(new WorkspaceMessage { Table = message }, cancellationToken).AsTask();
}
