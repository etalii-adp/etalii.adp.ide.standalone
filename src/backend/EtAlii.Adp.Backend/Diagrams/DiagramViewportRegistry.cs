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
    /// <summary>An open stream registers the callback its viewport reports should reach.</summary>
    void Register(ShortGuid watchId, string bodyPath, Action<DiagramViewport> onReported);

    /// <summary>Delivers a reported viewport to the matching open stream; false when there is none.</summary>
    bool Report(ShortGuid watchId, string bodyPath, DiagramViewport viewport);

    /// <summary>The stream is closing; drop its registration.</summary>
    void Remove(ShortGuid watchId, string bodyPath);
}

/// <inheritdoc />
public sealed class DiagramViewportRegistry : IDiagramViewportRegistry
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, string BodyPath), Action<DiagramViewport>> _byConnection = new();

    public void Register(ShortGuid watchId, string bodyPath, Action<DiagramViewport> onReported) =>
        _byConnection[Key(watchId, bodyPath)] = onReported;

    public bool Report(ShortGuid watchId, string bodyPath, DiagramViewport viewport)
    {
        if (!_byConnection.TryGetValue(Key(watchId, bodyPath), out var onReported))
        {
            return false;
        }

        onReported(viewport);
        return true;
    }

    public void Remove(ShortGuid watchId, string bodyPath) => _byConnection.TryRemove(Key(watchId, bodyPath), out _);

    private static (ShortGuid, string) Key(ShortGuid watchId, string bodyPath) => (watchId, bodyPath.ToUpperInvariant());
}
