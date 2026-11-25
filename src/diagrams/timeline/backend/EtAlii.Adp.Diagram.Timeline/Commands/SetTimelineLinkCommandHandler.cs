using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Handles <see cref="SetTimelineLinkCommand"/>.</summary>
public sealed class SetTimelineLinkCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<SetTimelineLinkCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, SetTimelineLinkCommand command)
    {
        if (TimelineEdits.ElementOf(model, command.ElementId) is not { Link: not null } element)
        {
            return EditOutcome.Refused(TimelineEdits.ElementGone);
        }

        // Without its link the entry is read by the plain period and moment rules, whose label it then takes.
        return command.Link is null
            ? TimelineEdits.Set(body, element, ("link", null), ("label", command.Label))
            : TimelineEdits.Set(body, element, ("link", command.Link));
    }
}
