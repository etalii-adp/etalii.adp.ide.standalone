using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a rename. The inverse is the same command carrying the previous label.</summary>
public sealed class RenameTimelineElementCommandHandler : ICommandHandler<RenameTimelineElementCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RenameTimelineElementCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameTimelineElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be renamed until the file is fixed."));
        }

        var element = TimelineEdits.ElementOf(entry.Model, command.ElementId);
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this timeline."));
        }

        var inverse = new RenameTimelineElementCommand(command.BodyPath, command.ElementId, element.Label);
        TimelineWriter.SetLabel(entry.Document, element, command.Label);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(error));
    }
}
