using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a removal. The inverse is a byte-exact restore of the removed lines, captured
/// before anything is spliced - not a re-creation from the model, which would lose the comments
/// and formatting the author had inside the removed block.
/// </summary>
public sealed class RemoveTimelineElementCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<RemoveTimelineElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, RemoveTimelineElementCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused("That element is no longer in this timeline.");
        }

        // Captured in ascending order before any splice moves a line, so the restore can replay
        // them ascending against the document as it was.
        var segments = TimelineWriter.ConnectionsTouching(model, element.Id)
            .Select(connection => connection.Range)
            .Append(element.Range)
            .OrderBy(range => range.Start)
            .Select(range => TimelineEdits.Capture(body, range))
            .ToList();

        TimelineWriter.RemoveElement(body, model, element);

        if (command.RemoveEmptiedConnectionsSection)
        {
            // This removal is the undo of an insert that created the connections: key on demand,
            // so the key it created goes too - or the undo comes back one line different.
            TimelineWriter.RemoveConnectionsSectionIfEmpty(body, TimelineEdits.Read(body));
        }

        // Redoing the restore removes the element again - by id, which it has again by then.
        return EditOutcome.UndoneBy(new RestoreLinesCommand<ITimelineDocumentStore>(
            command.BodyPath, segments, new RemoveTimelineElementCommand(command.BodyPath, command.ElementId)));
    }
}
