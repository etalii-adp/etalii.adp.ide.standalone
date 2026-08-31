using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Puts one element somewhere: a begin, an end, a row. The one command behind a drag, a resize
/// and a grid edit, whose inverse is the same command carrying the previous three values.
/// </summary>
/// <remarks>
/// One command rather than five differing only in which field they carry - the design flags this
/// as the judgement a reviewer may make the other way. The cost is that the history cannot tell
/// a move from a resize by type, so <paramref name="Description"/> carries what the gesture was
/// and travels into the inverse unchanged.
/// </remarks>
/// <param name="BodyPath">The document the element lives in.</param>
/// <param name="ElementId">Which element.</param>
/// <param name="Begin">The new begin, as document text.</param>
/// <param name="End">The new end as document text, or null for a moment - which never grows one here.</param>
/// <param name="Row">The new row.</param>
/// <param name="Description">What the gesture was - "Moved", "Resized", "Edited" - for the history.</param>
public sealed record SetTimelinePlacementCommand(
    string BodyPath,
    string ElementId,
    string Begin,
    string? End,
    int Row,
    string Description) : ICommand;
