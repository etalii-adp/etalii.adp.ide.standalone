using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out an end change. The inverse is the same command carrying the previous end.</summary>
public sealed class SetTimelineEndCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<SetTimelineEndCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, SetTimelineEndCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused("That element is no longer in this timeline.");
        }

        if (command.End is not null && element.Begin.IsReadable)
        {
            var end = TimelineInstants.Parse(command.End);
            if (end is null)
            {
                return EditOutcome.Refused($"'{command.End}' is not a time this timeline can read.");
            }

            if (end < element.Begin.Value)
            {
                // The third path Requirement 3.4 closes, alongside the adorner and the grid.
                return EditOutcome.Refused("An element cannot end before it begins.");
            }
        }

        TimelineWriter.SetEnd(body, element, command.End);
        return EditOutcome.UndoneBy(command with { End = element.End?.Text });
    }
}
