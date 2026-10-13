using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a disconnect. The inverse is a connect carrying the same id, endpoints and
/// label - sufficient because a connection block is machine-shaped; an element's block, which
/// can hold an author's comments, gets the byte-exact restore instead.
/// </summary>
public sealed class DisconnectTimelineConnectionCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<DisconnectTimelineConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, DisconnectTimelineConnectionCommand command)
    {
        var connection = TimelineEdits.ConnectionOf(model, command.ConnectionId);
        if (connection is null)
        {
            return EditOutcome.Refused("That relation is no longer in this timeline.");
        }

        TimelineWriter.RemoveConnection(body, connection);

        if (command.RemoveEmptiedConnectionsSection)
        {
            // The undo of a connect that created the connections: key on demand.
            TimelineWriter.RemoveConnectionsSectionIfEmpty(body, TimelineParser.Parse(body));
        }

        return EditOutcome.UndoneBy(new ConnectTimelineElementsCommand(
            command.BodyPath, connection.Id, connection.From, connection.To, connection.Label));
    }
}
