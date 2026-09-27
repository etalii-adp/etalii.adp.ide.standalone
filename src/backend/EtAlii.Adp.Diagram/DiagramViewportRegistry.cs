namespace EtAlii.Adp.Diagram;

/// <inheritdoc />
/// <remarks>
/// <b>More than one stream may hold a key at once.</b> The client's development remount opens a
/// second stream for a diagram on the same connection before the first has closed, and either may
/// close first. So each key keeps every open stream's registration, in the order they opened, and
/// answers with the latest one still open. A single slot overwritten by the second open and emptied
/// by whichever closed left the other stream unfindable: every move and every view report answered
/// "no open stream" while the canvas stayed on screen (Peter's report, 2026-09-27).
/// </remarks>
public sealed class DiagramViewportRegistry : IDiagramViewportRegistry
{
    private sealed record Registration(IDiagramSession Session, Action<DiagramViewport> OnReported);

    private readonly Lock _lock = new();
    private readonly Dictionary<(ShortGuid WatchId, string BodyPath), List<Registration>> _byConnection = [];

    public void Register(
        ShortGuid watchId,
        string bodyPath,
        IDiagramSession session,
        Action<DiagramViewport> onReported)
    {
        lock (_lock)
        {
            var key = Key(watchId, bodyPath);
            if (!_byConnection.TryGetValue(key, out var registrations))
            {
                _byConnection[key] = registrations = [];
            }

            registrations.Add(new Registration(session, onReported));
        }
    }

    public bool Report(
        ShortGuid watchId,
        string bodyPath,
        DiagramViewport viewport)
    {
        var latest = Latest(watchId, bodyPath);
        if (latest is null)
        {
            return false;
        }

        latest.OnReported(viewport);
        return true;
    }

    public IDiagramSession? Find(ShortGuid watchId, string bodyPath) => Latest(watchId, bodyPath)?.Session;

    public void Remove(ShortGuid watchId, string bodyPath, IDiagramSession session)
    {
        lock (_lock)
        {
            var key = Key(watchId, bodyPath);
            if (!_byConnection.TryGetValue(key, out var registrations))
            {
                return;
            }

            // The closing stream's own registration, and no other: the last one it made, should the
            // same session have been registered twice.
            var index = registrations.FindLastIndex(registration => ReferenceEquals(registration.Session, session));
            if (index >= 0)
            {
                registrations.RemoveAt(index);
            }

            if (registrations.Count == 0)
            {
                _byConnection.Remove(key);
            }
        }
    }

    private Registration? Latest(ShortGuid watchId, string bodyPath)
    {
        lock (_lock)
        {
            return _byConnection.TryGetValue(Key(watchId, bodyPath), out var registrations) ? registrations[^1] : null;
        }
    }

    private static (ShortGuid, string) Key(ShortGuid watchId, string bodyPath) => (watchId, bodyPath.ToUpperInvariant());
}
