using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out an add: one entry where the binding puts a new one, a period when it has an end.</summary>
public sealed class AddTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<AddTimelineElementCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(AddTimelineElementCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Id))
        {
            return CommandResult.Failure("An element needs an id.");
        }

        if (string.IsNullOrWhiteSpace(command.Begin))
        {
            return CommandResult.Failure("An element needs a begin.");
        }

        // Requirement 3.4 covers creation too: an element born inverted is as unreachable
        // through the application as one edited into that state.
        return Inverted(command)
            ? CommandResult.Failure("An element cannot end before it begins.")
            : await base.RunAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, AddTimelineElementCommand command)
    {
        if (TimelineEdits.ElementOf(model, command.Id) is not null)
        {
            // Redo lands here when the element survived - re-adding it would duplicate the id.
            return EditOutcome.Refused("An element with that id is already in this timeline.");
        }

        return TimelineEdits.AddElement(body, command.Id, command.Label, command.Begin, command.End, command.Row);
    }

    private static bool Inverted(AddTimelineElementCommand command)
    {
        if (command.End is null)
        {
            return false;
        }

        var begin = TimelineInstants.Parse(command.Begin);
        var end = TimelineInstants.Parse(command.End);
        return begin is not null && end is not null && end < begin;
    }
}
