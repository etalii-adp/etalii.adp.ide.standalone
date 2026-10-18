using System.Collections.Concurrent;
using System.Threading.Channels;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The tabs whose <see cref="WorkspaceService"/> stream is open, keyed by watch id, so a unary
/// <c>OpenDiagram</c> can find the stream its deltas are to ride and <c>CloseDiagram</c> the pump
/// it is to stop. Per-connection state that dies with the stream, like every other store keyed by
/// watch id.
/// </summary>
public sealed class WorkspaceConnections
{
    private static readonly ILogger _logger = Log.ForContext<WorkspaceConnections>();

    private readonly ConcurrentDictionary<ShortGuid, WorkspaceConnection> _connections = new();

    /// <summary>
    /// Registers a connection, replacing one a reconnect left behind under the same id - the older
    /// one is ending anyway, and disposing it will not touch its replacement. Disposing the connection
    /// removes it; the stream holds it in its connection's scope (code-structure FR-067).
    /// </summary>
    public WorkspaceConnection Register(ShortGuid watchId, ShortGuid userId, ChannelWriter<WorkspaceMessage> writer, CancellationToken lifetime)
    {
        var connection = new WorkspaceConnection(watchId, userId, writer, lifetime, Remove);
        _connections[watchId] = connection;
        return connection;
    }

    /// <summary>
    /// The open connection under <paramref name="watchId"/> if it is <paramref name="userId"/>'s, or
    /// null: another user's connection is no more this caller's than one that is not open. The one
    /// place that question is answered (code-structure FR-069).
    /// </summary>
    public WorkspaceConnection? Find(ShortGuid watchId, ShortGuid userId) =>
        _connections.GetValueOrDefault(watchId) is { } connection && connection.UserId == userId ? connection : null;

    /// <summary>
    /// The open connection under <paramref name="watchId"/> of <paramref name="userId"/>, for opening a
    /// stream on it, or a transient refusal: the client waits for the connection's baseline before it
    /// opens anything, so a connection that is not open is a reconnect racing a close.
    /// </summary>
    public WorkspaceConnection Require(ShortGuid watchId, ShortGuid userId)
    {
        if (Find(watchId, userId) is { } connection)
        {
            return connection;
        }

        _logger.Debug("Refused a stream on watch {WatchId}: no workspace stream of this user is open under it", watchId);
        throw new RpcException(new Status(StatusCode.Unavailable, "The connection is not open."));
    }

    /// <summary>Removes exactly this connection, and stops every stream it carried.</summary>
    private void Remove(WorkspaceConnection connection)
    {
        _connections.TryRemove(new KeyValuePair<ShortGuid, WorkspaceConnection>(connection.WatchId, connection));
        connection.StopAll();
    }
}

/// <summary>
/// One tab's stream: who opened it, where its messages go, and the diagram and table streams running on it.
/// </summary>
public sealed class WorkspaceConnection(ShortGuid watchId, ShortGuid userId, ChannelWriter<WorkspaceMessage> writer, CancellationToken lifetime, Action<WorkspaceConnection> remove) : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<WorkspaceConnection>();

    private readonly ConcurrentDictionary<ShortGuid, CancellationTokenSource> _streams = new();
    // Keyed by the stream's source, which the pump knows and its task does not; null for the moment
    // between a pump starting and its task being known.
    private readonly ConcurrentDictionary<CancellationTokenSource, Task?> _pumps = new();

    public ShortGuid WatchId { get; } = watchId;

    public ShortGuid UserId { get; } = userId;

    public ChannelWriter<WorkspaceMessage> Writer { get; } = writer;

    /// <summary>
    /// Opens a stream on this connection: runs <paramref name="map"/>'s pump over
    /// <paramref name="session"/> until the stream is stopped or the connection ends, tells the client
    /// when it ends on its own, and forgets it when it returns (code-structure FR-069).
    /// </summary>
    /// <remarks>
    /// The session is the pump's from here: it disposes it when it returns. When the id is already
    /// running on this connection, the stream never starts, the session is disposed here and the call
    /// is refused. The pump runs up to its first wait before this returns, so what it records as it
    /// starts is in place before the open is answered.
    /// </remarks>
    public async Task OpenStreamAsync<TSession>(ShortGuid streamId, TSession session, StreamMap<TSession> map)
        where TSession : IAsyncDisposable
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        if (!_streams.TryAdd(streamId, source))
        {
            source.Dispose();
            await session.DisposeAsync();
            throw new RpcException(new Status(StatusCode.AlreadyExists, "A stream with this id is already open."));
        }

        // Recorded before the pump starts and filled in after, so a pump that has already returned
        // and removed its entry leaves nothing behind.
        _pumps[source] = null;
        var pump = PumpAsync(streamId, session, map, source);
        _pumps.TryUpdate(source, pump, null);
    }

    /// <summary>Stops one stream. False when it is not running, which is not an error.</summary>
    public bool Stop(ShortGuid streamId)
    {
        if (!_streams.TryRemove(streamId, out var source))
        {
            return false;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The pump failed and returned between the removal above and this line; it is stopped.
        }

        return true;
    }

    /// <summary>The number of streams running on this connection.</summary>
    public int StreamCount => _streams.Count;

    /// <summary>
    /// Stops every stream this connection carries and waits until their pumps have returned, so
    /// nothing a stream holds outlives the connection that carried it.
    /// </summary>
    public async Task CloseAsync()
    {
        StopAll();
        await Task.WhenAll(_pumps.Values.OfType<Task>());
    }

    /// <summary>Removes the connection from the open ones and stops every stream it carried.</summary>
    public void Dispose() => remove(this);

    internal void StopAll()
    {
        foreach (var streamId in _streams.Keys)
        {
            Stop(streamId);
        }
    }

    /// <summary>Never throws: every way a pump can end is answered here.</summary>
    private async Task PumpAsync<TSession>(ShortGuid streamId, TSession session, StreamMap<TSession> map, CancellationTokenSource source)
    {
        Documents.Wire.ShortGuid wireStreamId = streamId;
        try
        {
            await map.RunAsync(session, this, wireStreamId, source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            _logger.Debug("{Kind} stream {StreamId} on watch {WatchId} closed", map.Kind, streamId, WatchId);
        }
        catch (RpcException refusal) when (map.Refused is not null && PermanentRefusal.IsPermanent(refusal.StatusCode))
        {
            // Refused for good after the open was answered - a baseline that could not be made.
            // Ending the stream would read as a drop the client re-opens every RECONNECT_DELAY_MS
            // for ever; the refusal is what its own Open call would have ended with.
            _logger.Debug("{Kind} stream {StreamId} on watch {WatchId} refused: {Reason}", map.Kind, streamId, WatchId, refusal.Status.Detail);
            Writer.TryWrite(map.Refused(wireStreamId, refusal));
        }
        catch (Exception exception)
        {
            // The session failed on its own. Its own Open call would have ended in an error the
            // client re-opens from; here the connection carries on, so the stream says it ended.
            _logger.Warning(exception, "{Kind} stream {StreamId} on watch {WatchId} failed; telling the client it ended", map.Kind, streamId, WatchId);
            Writer.TryWrite(map.Ended(wireStreamId));
        }
        finally
        {
            _streams.TryRemove(new KeyValuePair<ShortGuid, CancellationTokenSource>(streamId, source));
            source.Dispose();
            _pumps.TryRemove(source, out _);
        }
    }
}
