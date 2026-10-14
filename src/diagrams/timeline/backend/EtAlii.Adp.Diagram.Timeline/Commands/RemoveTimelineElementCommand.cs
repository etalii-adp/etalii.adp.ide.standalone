

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Removes one element and every connection that references it, as one command with one undo
/// (Requirement 2.5).
/// </summary>
/// <param name="BodyPath">The document to remove from.</param>
/// <param name="ElementId">Which element.</param>
/// <param name="RemoveEmptiedConnectionsSection">No longer read: an undo puts back the text the edit started from (<see cref="RestoreDocumentCommand{TStore}"/>), so no inverse removes a <c>connections:</c> key; kept so the record keeps its shape.</param>
public sealed record RemoveTimelineElementCommand(
    string BodyPath,
    string ElementId,
    // ReSharper disable once NotAccessedPositionalProperty.Global - Reason: an undo now restores the text before the edit, so nothing reads RemoveEmptiedConnectionsSection; it is kept because timeline-disl-fbl task 6 keeps every command record as it was.
    bool RemoveEmptiedConnectionsSection = false) : IDocumentEditCommand;
