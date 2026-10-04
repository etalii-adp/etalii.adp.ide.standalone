using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Gives every element the row <see cref="TimelineArrangement"/> chooses, as one undo.</summary>
/// <param name="BodyPath">The timeline.</param>
public sealed record ArrangeTimelineCommand(string BodyPath) : ICommand;

/// <summary>
/// Puts elements on the given rows - the inverse of an arrangement, and its redo, since an undone
/// arrangement is put back exactly rather than computed again.
/// </summary>
/// <param name="BodyPath">The timeline.</param>
/// <param name="Rows">Each element's row, by id.</param>
public sealed record SetTimelineRowsCommand(string BodyPath, IReadOnlyDictionary<string, int> Rows) : ICommand;
