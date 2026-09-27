using System.Collections.Concurrent;
using System.Threading.Channels;
using EtAlii.Adp.Diagram.Wire;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The tabs whose <see cref="WorkspaceService"/> stream is open, keyed by watch id, so a unary
/// <c>OpenDiagram</c> can find the stream its deltas are to ride and <c>CloseDiagram</c> the pump
/// it is to stop. Per-connection state that dies with the stream, like every other store keyed by
/// watch id.
/// </summary>
public sealed class WorkspaceConnections
{
    private readonly ConcurrentDictionary<ShortGuid, WorkspaceConnection> _connections = new();

    /// <summary>
    /// Registers a connection, replacing one a reconnect left behind under the same id - the older
    /// one is ending anyway, and its own <see cref="Remove"/> will not touch its replacement.
    /// </summary>
    public WorkspaceConnection Register(ShortGuid watchId, ShortGuid userId, ChannelWriter<WorkspaceMessage> writer, CancellationToken lifetime)
    {
        var connection = new WorkspaceConnection(watchId, userId, writer, lifetime);
        _connections[watchId] = connection;
        return connection;
    }

    public WorkspaceConnection? Find(ShortGuid watchId) => _connections.GetValueOrDefault(watchId);

    /// <summary>Removes exactly this connection, and stops every diagram stream it carried.</summary>
    public void Remove(WorkspaceConnection connection)
    {
        _connections.TryRemove(new KeyValuePair<ShortGuid, WorkspaceConnection>(connection.WatchId, connection));
        connection.StopAll();
    }
}

/// <summary>
/// One tab's stream: who opened it, where its messages go, and the diagram streams running on it.
/// </summary>
public sealed class WorkspaceConnection(ShortGuid watchId, ShortGuid userId, ChannelWriter<WorkspaceMessage> writer, CancellationToken lifetime)
{
    private readonly ConcurrentDictionary<ShortGuid, CancellationTokenSource> _diagramStreams = new();

    public ShortGuid WatchId { get; } = watchId;

    public ShortGuid UserId { get; } = userId;

    public ChannelWriter<WorkspaceMessage> Writer { get; } = writer;

    /// <summary>
    /// Starts tracking a diagram stream, cancelled by <see cref="Stop"/>, by <see cref="StopAll"/>,
    /// or by the connection ending. Null when the id is already running on this connection.
    /// </summary>
    public CancellationTokenSource? TryStart(ShortGuid streamId)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        if (_diagramStreams.TryAdd(streamId, source))
        {
            return source;
        }

        source.Dispose();
        return null;
    }

    /// <summary>Stops one diagram stream. False when it is not running, which is not an error.</summary>
    public bool Stop(ShortGuid streamId)
    {
        if (!_diagramStreams.TryRemove(streamId, out var source))
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

    /// <summary>Forgets a diagram stream whose pump has returned; its source is the pump's to dispose.</summary>
    public void Forget(ShortGuid streamId, CancellationTokenSource source) =>
        _diagramStreams.TryRemove(new KeyValuePair<ShortGuid, CancellationTokenSource>(streamId, source));

    /// <summary>The number of diagram streams running on this connection.</summary>
    public int DiagramStreamCount => _diagramStreams.Count;

    internal void StopAll()
    {
        foreach (var streamId in _diagramStreams.Keys)
        {
            Stop(streamId);
        }
    }
}
