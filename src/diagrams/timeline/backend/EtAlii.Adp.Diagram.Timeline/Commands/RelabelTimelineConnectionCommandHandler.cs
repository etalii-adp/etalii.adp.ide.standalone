using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a relabel. The inverse is the same command carrying the previous label.</summary>
public sealed class RelabelTimelineConnectionCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<RelabelTimelineConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, RelabelTimelineConnectionCommand command)
    {
        var connection = TimelineEdits.ConnectionOf(model, command.ConnectionId);
        if (connection is null)
        {
            return EditOutcome.Refused("That relation is no longer in this timeline.");
        }

        TimelineWriter.SetConnectionLabel(body, connection, command.Label);
        return EditOutcome.UndoneBy(command with { Label = connection.Label });
    }
}
