using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a relabel: one change of the relation's label; a label cleared removes the key.</summary>
public sealed class RelabelTimelineConnectionCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<RelabelTimelineConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, RelabelTimelineConnectionCommand command)
    {
        var connection = TimelineEdits.ConnectionOf(model, command.ConnectionId);
        return connection is null
            ? EditOutcome.Refused(TimelineEdits.RelationGone)
            : TimelineEdits.Set(body, connection, ("label", command.Label));
    }
}
