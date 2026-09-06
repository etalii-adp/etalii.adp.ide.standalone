using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a removal. The inverse is a byte-exact restore of the removed lines, captured
/// before anything is spliced - not a re-creation from the model, which would lose the comments
/// and formatting the author had inside the removed block.
/// </summary>
public sealed class RemoveTimelineElementCommandHandler : ICommandHandler<RemoveTimelineElementCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RemoveTimelineElementCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveTimelineElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be removed until the file is fixed."));
        }

        var element = TimelineEdits.ElementOf(entry.Model, command.ElementId);
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this timeline."));
        }

        // Captured in ascending order before any splice moves a line, so the restore can replay
        // them ascending against the document as it was.
        var going = TimelineWriter.ConnectionsTouching(entry.Model, element.Id);
        var segments = going
            .Select(connection => connection.Range)
            .Append(element.Range)
            .OrderBy(range => range.Start)
            .Select(range => TimelineEdits.Capture(entry.Document, range))
            .ToList();

        TimelineWriter.RemoveElement(entry.Document, entry.Model, element);

        if (command.RemoveEmptiedConnectionsSection)
        {
            // This removal is the undo of an insert that created the connections: key on demand,
            // so the key it created goes too - or the undo comes back one line different.
            TimelineWriter.RemoveConnectionsSectionIfEmpty(entry.Document, TimelineParser.Parse(entry.Document));
        }

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new RestoreTimelineLinesCommand(command.BodyPath, command.ElementId, segments))
            : CommandResult.Failure(error));
    }
}
