using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a create-and-relate. The inverse is the removal of the new element, whose
/// relations - including the one made here - go with it.
/// </summary>
public sealed class AddConnectedTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<AddConnectedTimelineElementCommand>(documents)
{
    /// <summary>What an element added with its relation is called when the command names no label.</summary>
    public const string NewElementLabel = "New element";

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, AddConnectedTimelineElementCommand command)
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

        TimelineWriter.InsertElement(
            body, model, command.NewElementId, command.Label ?? NewElementLabel, command.Begin, command.End, command.Row);

        // The insert moved lines, so the relation is spliced against a fresh parse rather than
        // the ranges the first model recorded.
        var reparsed = TimelineParser.Parse(body);
        // Whether the insert is about to create the connections: key, so the inverse can take
        // the key back out and the undo return the file byte for byte.
        var hadSection = TimelineWriter.HasConnectionsSection(body);

        // A begin-anchor gesture runs the relation the other way: out of the new element's end
        // and into the existing one's start.
        (string source, string target) = command.NewElementIsSource
            ? (command.NewElementId, command.FromElementId)
            : (command.FromElementId, command.NewElementId);
        TimelineWriter.InsertConnection(body, reparsed, command.RelationId, source, target, "");

        return EditOutcome.UndoneBy(new RemoveTimelineElementCommand(
            command.BodyPath, command.NewElementId, RemoveEmptiedConnectionsSection: !hadSection));
    }
}
