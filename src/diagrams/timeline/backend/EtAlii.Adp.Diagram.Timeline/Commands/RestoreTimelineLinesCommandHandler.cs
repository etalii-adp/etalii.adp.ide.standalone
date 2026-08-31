using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a restore. Segments were captured ascending against the document as it was, so
/// replaying them ascending rebuilds exactly that document: by the time each segment is
/// inserted, every earlier one is back and its index means what it meant.
/// </summary>
public sealed class RestoreTimelineLinesCommandHandler : ICommandHandler<RestoreTimelineLinesCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RestoreTimelineLinesCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RestoreTimelineLinesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be restored until the file is fixed."));
        }

        foreach (var segment in command.Segments)
        {
            if (segment.Start > entry.Document.Lines.Count)
            {
                // The file has been cut down under us since the removal, so there is no longer a
                // place to put these back. Refusing is honest; guessing a position is not.
                return Task.FromResult(CommandResult.Failure(
                    "This timeline has changed too much since then to put that back."));
            }

            entry.Document.Insert(segment.Start, segment.Lines);
        }

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            // Redoing the undo removes it again - by id, which the element has again now.
            ? CommandResult.Success(new RemoveTimelineElementCommand(command.BodyPath, command.ElementId))
            : CommandResult.Failure(error));
    }
}
