using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a create-and-relate: two changes planned on one body, the element and then the relation
/// that reaches it, stored once, so the gesture is one undo.
/// </summary>
public sealed class AddConnectedTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<AddConnectedTimelineElementCommand>(documents)
{
    /// <summary>What an element added with its relation is called when the command names no label.</summary>
    public const string NewElementLabel = "New element";

    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, AddConnectedTimelineElementCommand command)
    {
        if (TimelineEdits.ElementOf(model, command.FromElementId) is null)
        {
            return EditOutcome.Refused("The element this relation starts from is no longer in this timeline.");
        }

        if (TimelineEdits.ElementOf(model, command.NewElementId) is not null)
        {
            // Redo lands here when the element survived - re-adding would duplicate the id.
            return EditOutcome.Refused("An element with that id is already in this timeline.");
        }

        var added = TimelineEdits.AddElement(
            body, command.NewElementId, command.Label ?? NewElementLabel, command.Begin, command.End, command.Row);
        if (!added.WasApplied)
        {
            return added;
        }

        // A begin-anchor gesture runs the relation the other way: out of the new element's end
        // and into the existing one's start.
        (string source, string target) = command.NewElementIsSource
            ? (command.NewElementId, command.FromElementId)
            : (command.FromElementId, command.NewElementId);
        // Planned against the body as the element left it: the library reads it again after a change.
        return TimelineEdits.AddConnection(body, command.RelationId, source, target, "");
    }
}
