using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a disconnect: the relation's entry is removed, and its undo is the shared restore.</summary>
public sealed class DisconnectTimelineConnectionCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<DisconnectTimelineConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, DisconnectTimelineConnectionCommand command)
    {
        var connection = TimelineEdits.ConnectionOf(model, command.ConnectionId);
        return connection is null
            ? EditOutcome.Refused(TimelineEdits.RelationGone)
            : TimelineEdits.Remove(body, connection);
    }
}
