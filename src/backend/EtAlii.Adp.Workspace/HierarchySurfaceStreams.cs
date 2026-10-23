using System.Collections.Concurrent;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The hierarchy surface streams that are running, by connection and stream id: where a gesture that follows the open
/// finds the session it is for, as <see cref="TableStreams"/> is for a table.
/// </summary>
/// <remarks>
/// A session is in here exactly while its stream runs. The pump that started it removes it when the stream ends,
/// however it ends, so a gesture for a stream that is gone finds nothing rather than a disposed session.
/// </remarks>
public sealed class HierarchySurfaceStreams
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, ShortGuid StreamId), IHierarchySurfaceSession> _sessions = new();

    public void Add(ShortGuid watchId, ShortGuid streamId, IHierarchySurfaceSession session) => _sessions[(watchId, streamId)] = session;

    public IHierarchySurfaceSession? Find(ShortGuid watchId, ShortGuid streamId) => _sessions.GetValueOrDefault((watchId, streamId));

    public void Remove(ShortGuid watchId, ShortGuid streamId, IHierarchySurfaceSession session) =>
        _sessions.TryRemove(new KeyValuePair<(ShortGuid, ShortGuid), IHierarchySurfaceSession>((watchId, streamId), session));

    /// <summary>How many hierarchy surface streams are running, across all connections.</summary>
    public int Count => _sessions.Count;
}
