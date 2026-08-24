using System.Collections.Concurrent;

namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>
/// Carries a <c>UpdateView</c> call, which arrives on its own unary request, to the
/// <c>Open</c> stream it belongs to - the two correlated one-way legs of the diagram
/// connection, joined by <c>(watch_id, body path)</c> just as the hierarchy and context
/// calls are joined by <c>watch_id</c>.
/// </summary>
public interface IDiagramViewportRegistry
{
    /// <summary>An open stream registers its session and the callback its viewport reports should reach.</summary>
    void Register(ShortGuid watchId, string bodyPath, IDiagramSession session, Action<DiagramViewport> onReported);

    /// <summary>Delivers a reported viewport to the matching open stream; false when there is none.</summary>
    bool Report(ShortGuid watchId, string bodyPath, DiagramViewport viewport);

    /// <summary>
    /// The open session behind a unary call - what lets a MoveElement arriving on its own
    /// HTTP request reach the stream's module session, exactly as Report does for viewports.
    /// Null when this connection has no open stream for the diagram.
    /// </summary>
    IDiagramSession? Find(ShortGuid watchId, string bodyPath);

    /// <summary>The stream is closing; drop its registration.</summary>
    void Remove(ShortGuid watchId, string bodyPath);
}

/// <inheritdoc />
public sealed class DiagramViewportRegistry : IDiagramViewportRegistry
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, string BodyPath), (IDiagramSession Session, Action<DiagramViewport> OnReported)> _byConnection = new();

    public void Register(
        ShortGuid watchId,
        string bodyPath,
        IDiagramSession session,
        Action<DiagramViewport> onReported) =>
        _byConnection[Key(watchId, bodyPath)] = (session, onReported);

    public bool Report(
        ShortGuid watchId,
        string bodyPath,
        DiagramViewport viewport)
    {
        if (!_byConnection.TryGetValue(Key(watchId, bodyPath), out var entry))
        {
            return false;
        }

        entry.OnReported(viewport);
        return true;
    }

    public IDiagramSession? Find(ShortGuid watchId, string bodyPath) =>
        _byConnection.TryGetValue(Key(watchId, bodyPath), out var entry) ? entry.Session : null;

    public void Remove(ShortGuid watchId, string bodyPath) => _byConnection.TryRemove(Key(watchId, bodyPath), out _);

    private static (ShortGuid, string) Key(ShortGuid watchId, string bodyPath) => (watchId, bodyPath.ToUpperInvariant());
}
