using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a rename. The inverse is the same command carrying the previous label.</summary>
public sealed class RenameTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<RenameTimelineElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, RenameTimelineElementCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused("That element is no longer in this timeline.");
        }

        TimelineWriter.SetLabel(body, element, command.Label);
        return EditOutcome.UndoneBy(command with { Label = element.Label });
    }
}
