using System.Collections.Concurrent;

namespace EtAlii.Adp.Backend.Diagrams;

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
