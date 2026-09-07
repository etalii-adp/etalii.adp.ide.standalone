

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Renames one element - one line rewritten, identity untouched, so the selection, the history
/// and every pushed element id survive it. The benefit the design attributes to owning the
/// schema: the id is in the file, and a rename has no reason to go near it.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementId">Which element.</param>
/// <param name="Label">The new label.</param>
public sealed record RenameTimelineElementCommand(
    string BodyPath,
    string ElementId,
    string Label) : ICommand;
