using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Gives every element the row <see cref="TimelineArrangement"/> chooses, as one undo.</summary>
/// <param name="BodyPath">The timeline.</param>
public sealed record ArrangeTimelineCommand(string BodyPath) : IDocumentEditCommand;

/// <summary>
/// Puts elements on the given rows, as one undo. An arrangement is undone by putting back the text
/// it started from and redone by running it again, so this is no longer its inverse.
/// </summary>
/// <param name="BodyPath">The timeline.</param>
/// <param name="Rows">Each element's row, by id.</param>
// ReSharper disable once ClassNeverInstantiated.Global - Reason: an arrangement was undone by this command until timeline-disl-fbl task 6, which keeps every command record and its handler; the history now restores the text instead, so only the tests send it.
public sealed record SetTimelineRowsCommand(string BodyPath, IReadOnlyDictionary<string, int> Rows) : IDocumentEditCommand;
