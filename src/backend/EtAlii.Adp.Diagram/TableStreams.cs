using System.Collections.Concurrent;
using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The table streams that are running, by connection and stream id: where a table call that
/// follows the open - the window, the view, an edit - finds the session it is for.
/// </summary>
/// <remarks>
/// A session is in here exactly while its stream runs. The pump that started it removes it when
/// the stream ends, however it ends, so a call for a stream that is gone finds nothing rather
/// than a disposed session.
/// </remarks>
public sealed class TableStreams
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, ShortGuid StreamId), IDesignerSession> _sessions = new();

    public void Add(ShortGuid watchId, ShortGuid streamId, IDesignerSession session) => _sessions[(watchId, streamId)] = session;

    public IDesignerSession? Find(ShortGuid watchId, ShortGuid streamId) => _sessions.GetValueOrDefault((watchId, streamId));

    public void Remove(ShortGuid watchId, ShortGuid streamId, IDesignerSession session) =>
        _sessions.TryRemove(new KeyValuePair<(ShortGuid, ShortGuid), IDesignerSession>((watchId, streamId), session));

    /// <summary>How many table streams are running, across all connections.</summary>
    public int Count => _sessions.Count;
}
