namespace EtAlii.Adp.Diagram;

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

    /// <summary>
    /// The stream is closing; drop its registration - and only its own. A second stream for the
    /// same diagram on the same connection may have registered in between, as the client's
    /// development remount opens one before the first has closed; removing by key alone would
    /// take that one's registration with it, and every move would find no open stream.
    /// </summary>
    void Remove(ShortGuid watchId, string bodyPath, IDiagramSession session);
}
