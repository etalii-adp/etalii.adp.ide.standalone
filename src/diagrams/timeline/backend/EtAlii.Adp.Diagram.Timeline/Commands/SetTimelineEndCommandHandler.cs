using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out an end change. The inverse is the same command carrying the previous end.</summary>
public sealed class SetTimelineEndCommandHandler : ICommandHandler<SetTimelineEndCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public SetTimelineEndCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetTimelineEndCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be edited until the file is fixed."));
        }

        var element = TimelineEdits.ElementOf(entry.Model, command.ElementId);
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this timeline."));
        }

        if (command.End is not null && element.Begin.IsReadable)
        {
            var end = TimelineInstants.Parse(command.End);
            if (end is null)
            {
                return Task.FromResult(CommandResult.Failure($"'{command.End}' is not a time this timeline can read."));
            }

            if (end < element.Begin.Value)
            {
                // The third path Requirement 3.4 closes, alongside the adorner and the grid.
                return Task.FromResult(CommandResult.Failure("An element cannot end before it begins."));
            }
        }

        var inverse = new SetTimelineEndCommand(command.BodyPath, command.ElementId, element.End?.Text);
        TimelineWriter.SetEnd(entry.Document, element, command.End);

        var saved = _documents.Save(command.BodyPath, entry);
        return Task.FromResult(!saved.Failed
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(saved.Error));
    }
}
