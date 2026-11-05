using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Proto = EtAlii.Adp.Designer.Wire;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The hierarchy surface's calls (DESL section 7.7, spec 029 R8): a page of nested boxes joins the tab's one stream,
/// leaves it, and takes the person's gestures. Like the table's calls, they name no designer type: the module's session
/// does the work, and this carries its read model to the connection.
/// </summary>
public sealed partial class DesignerService
{
    private readonly HierarchySurfaceStreams _surfaces;
    private readonly StreamMap<IHierarchySurfaceSession> _surfaceStream;

    public override async Task<Proto.OpenHierarchySurfaceResponse> OpenHierarchySurface(Proto.OpenHierarchySurfaceRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var connection = _connections.Require((ShortGuid)request.WatchId, userId);

        var session = await OpenSessionAsync<IHierarchySurfaceSession>(userId, connection.WatchId, request.ProjectId, request.Path, "hierarchy surface");

        await connection.OpenStreamAsync((ShortGuid)request.StreamId, session, _surfaceStream);
        return new Proto.OpenHierarchySurfaceResponse();
    }

    public override Task<Proto.CloseHierarchySurfaceResponse> CloseHierarchySurface(Proto.CloseHierarchySurfaceRequest request, ServerCallContext context)
    {
        var connection = _connections.Find((ShortGuid)request.WatchId, SessionContext.GetUserId(context));
        if (connection is not null && connection.Stop((ShortGuid)request.StreamId))
        {
            _logger.Debug("Closed hierarchy surface stream {StreamId} on watch {WatchId}", (ShortGuid)request.StreamId, (ShortGuid)request.WatchId);
        }

        return Task.FromResult(new Proto.CloseHierarchySurfaceResponse());
    }

    public override Task<Proto.HierarchySurfaceGestureResponse> HierarchySurfaceGesture(Proto.HierarchySurfaceGestureRequest request, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (_connections.Find(watchId, SessionContext.GetUserId(context)) is null || _surfaces.Find(watchId, (ShortGuid)request.StreamId) is not { } session)
        {
            // A gesture for a stream that is gone is a reconnect racing a close, and the client re-opens.
            throw new RpcException(new Status(StatusCode.Unavailable, "The page is not open."));
        }

        var gesture = HierarchySurfaceWire.FromProto(request);
        var error = session.Gesture((ShortGuid)request.EditId, gesture);
        if (error.Length > 0)
        {
            _logger.Debug("Refused the {Operation} gesture on hierarchy surface stream {StreamId}: {Reason}", gesture.Operation, (ShortGuid)request.StreamId, error);
        }

        return Task.FromResult(new Proto.HierarchySurfaceGestureResponse { Error = error });
    }

    /// <summary>
    /// Writes the session's model, then every model and outcome it raises, onto the connection's stream under
    /// <paramref name="streamId"/>, until the stream is closed or the connection ends. Disposes the session when it returns.
    /// </summary>
    private async Task RunSurfaceAsync(IHierarchySurfaceSession opened, WorkspaceConnection connection, Documents.Wire.ShortGuid streamId, CancellationToken cancellationToken)
    {
        // Before the first wait, so a gesture that follows the open finds the session.
        var id = (ShortGuid)streamId;
        _surfaces.Add(connection.WatchId, id, opened);
        // simplified: whole model per change, measured at 100 items and 500 requirements; send deltas when SC-003's test fails at a larger size
        var changes = StreamBound.Ending<HierarchySurfaceChangedEventArgs>($"Hierarchy surface stream {id} on watch {connection.WatchId}");

        await using var session = opened;
        session.Changed += OnChanged;
        try
        {
            await WriteAsync(connection, new Proto.HierarchySurfaceStreamMessage { StreamId = streamId, Model = HierarchySurfaceWire.ToProto(session.Model()) }, cancellationToken);
            _logger.Information("Opened hierarchy surface stream {StreamId} on watch {WatchId}", id, connection.WatchId);

            await foreach (var change in changes.Reader.ReadAllAsync(cancellationToken))
            {
                var message = new Proto.HierarchySurfaceStreamMessage { StreamId = streamId };
                if (change.Model is { } model)
                {
                    message.Model = HierarchySurfaceWire.ToProto(model);
                }
                else if (change.Outcome is { } outcome)
                {
                    message.Outcome = HierarchySurfaceWire.ToProto(outcome);
                }

                await WriteAsync(connection, message, cancellationToken);
            }
        }
        finally
        {
            session.Changed -= OnChanged;
            _surfaces.Remove(connection.WatchId, id, session);
        }

        return;

        // MUST STAY NON-BLOCKING: a session may raise this while holding its lock.
        void OnChanged(object? sender, HierarchySurfaceChangedEventArgs args) => changes.Writer.TryWrite(args);
    }

    private static Task WriteAsync(WorkspaceConnection connection, Proto.HierarchySurfaceStreamMessage message, CancellationToken cancellationToken) =>
        connection.Writer.WriteAsync(new WorkspaceMessage { HierarchySurface = message }, cancellationToken).AsTask();
}
