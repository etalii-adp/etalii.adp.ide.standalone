using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Creates one element and the relation that reaches it, as one command - what a relation
/// dragged onto empty space means: "this leads to something that does not exist yet".
/// </summary>
/// <remarks>
/// One command rather than an add followed by a connect, so the gesture is one history entry
/// and one undo - the inverse removes the new element, which takes its relation with it.
/// Both ids are the caller's to supply, so a redo re-creates both under the ids they had.
/// </remarks>
/// <param name="BodyPath">The document.</param>
/// <param name="FromElementId">The element the relation is dragged from.</param>
/// <param name="NewElementId">The id the new element is born under.</param>
/// <param name="RelationId">The id the relation is born under.</param>
/// <param name="Begin">Where the gesture landed, as document text.</param>
/// <param name="End">The new element's end, or null for a moment.</param>
/// <param name="Row">The row the gesture landed on.</param>
/// <param name="NewElementIsSource">
/// Whether the relation runs from the new element into the existing one - a gesture dragged
/// from an element's begin anchor, where what precedes it points into it - rather than the
/// other way round.
/// </param>
public sealed record AddConnectedTimelineElementCommand(
    string BodyPath,
    string FromElementId,
    string NewElementId,
    string RelationId,
    string Begin,
    string? End,
    int Row,
    bool NewElementIsSource = false) : ICommand;
