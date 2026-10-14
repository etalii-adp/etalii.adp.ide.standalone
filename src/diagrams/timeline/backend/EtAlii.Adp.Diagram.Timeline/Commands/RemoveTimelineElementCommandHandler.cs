using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a removal: the element's entry and every relation whose <c>from</c> or <c>to</c> holds its
/// written id, in one change (Requirement 3.5); the count asked beforehand is
/// <see cref="TimelineDefinition.RemoveConfirmation"/>'s. The undo is the shared restore of the text
/// before, so the author's comments and formatting inside the removed entries come back byte for byte.
/// </summary>
public sealed class RemoveTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<RemoveTimelineElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, RemoveTimelineElementCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        return element is null
            ? EditOutcome.Refused(TimelineEdits.ElementGone)
            : TimelineEdits.Remove(body, element);
    }
}
