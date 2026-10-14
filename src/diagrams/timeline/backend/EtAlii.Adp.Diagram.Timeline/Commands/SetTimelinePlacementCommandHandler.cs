using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a placement: locate against current state, refuse with a sentence when that
/// fails, and set the begin, the end of a period and the row as one change.
/// </summary>
/// <remarks>
/// <para>
/// The end-before-begin clamp lives here as well as in the canvas, and this copy is the one that
/// makes Requirement 3.4 true: the canvas clamp is what the user feels, but a guard only in the
/// client is a guard a second client walks around. With this and the grid's refusal (Requirement
/// 10.3), the only way a file contains an inverted element is that somebody edited the text by
/// hand.
/// </para>
/// <para>
/// Preconditions are checked against the document as it is now rather than trusted from when the
/// command was made, because undo and redo dispatch the same instance again later.
/// </para>
/// </remarks>
public sealed class SetTimelinePlacementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<SetTimelinePlacementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, SetTimelinePlacementCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused(TimelineEdits.ElementGone);
        }

        if (InvertsTheElement(command))
        {
            // Requirement 3.4: no path through the application may create end-before-begin.
            return EditOutcome.Refused("An element cannot end before it begins.");
        }

        // The end is set only for a period: a placement never gives an element an end it did not
        // have - that is the context menu's action, not a drag's side effect (Requirement 7.5). And
        // only when it moves: an end written empty is the period's own, and setting it to what it
        // holds would remove the key (the binding's empty: remove) and make the period a moment.
        return command.End is not null && element.End is not null && !string.Equals(command.End, element.End.Text, StringComparison.Ordinal)
            ? TimelineEdits.Set(body, element, ("begin", command.Begin), ("end", command.End), ("row", command.Row))
            : TimelineEdits.Set(body, element, ("begin", command.Begin), ("row", command.Row));
    }

    /// <summary>
    /// Whether the command's own values put its end before its begin. Judged from the command,
    /// not the document: the document's current state may already be inverted from a hand edit,
    /// and this command may be the one that fixes it.
    /// </summary>
    private static bool InvertsTheElement(SetTimelinePlacementCommand command)
    {
        if (command.End is null)
        {
            return false;
        }

        var begin = TimelineInstants.Parse(command.Begin);
        var end = TimelineInstants.Parse(command.End);

        // A value that will not parse cannot be judged; the rules report it, and refusing an
        // edit for a reason that is not true would block the user from fixing their file.
        return begin is not null && end is not null && end < begin;
    }
}
