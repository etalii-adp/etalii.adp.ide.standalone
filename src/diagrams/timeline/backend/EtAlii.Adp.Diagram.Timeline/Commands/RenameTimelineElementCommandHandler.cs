using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a rename: one change of the element's label, its value's span in its own style.</summary>
public sealed class RenameTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<RenameTimelineElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, RenameTimelineElementCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        return element is null
            ? EditOutcome.Refused(TimelineEdits.ElementGone)
            : TimelineEdits.Set(body, element, ("label", command.Label));
    }
}
