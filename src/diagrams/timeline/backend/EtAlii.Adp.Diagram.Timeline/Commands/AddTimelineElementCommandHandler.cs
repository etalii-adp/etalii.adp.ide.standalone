using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out an add. The inverse is the removal of what was added.</summary>
public sealed class AddTimelineElementCommandHandler : ICommandHandler<AddTimelineElementCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public AddTimelineElementCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddTimelineElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.Id))
        {
            return Task.FromResult(CommandResult.Failure("An element needs an id."));
        }

        if (string.IsNullOrWhiteSpace(command.Begin))
        {
            return Task.FromResult(CommandResult.Failure("An element needs a begin."));
        }

        if (Inverted(command))
        {
            // Requirement 3.4 covers creation too: an element born inverted is as unreachable
            // through the application as one edited into that state.
            return Task.FromResult(CommandResult.Failure("An element cannot end before it begins."));
        }

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be added until the file is fixed."));
        }

        if (TimelineEdits.ElementOf(entry.Model, command.Id) is not null)
        {
            // Redo lands here when the element survived - re-adding it would duplicate the id.
            return Task.FromResult(CommandResult.Failure("An element with that id is already in this timeline."));
        }

        TimelineWriter.InsertElement(
            entry.Document, entry.Model, command.Id, command.Label, command.Begin, command.End, command.Row);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new RemoveTimelineElementCommand(command.BodyPath, command.Id))
            : CommandResult.Failure(error));
    }

    private static bool Inverted(AddTimelineElementCommand command)
    {
        if (command.End is null)
        {
            return false;
        }

        var begin = TimelineInstants.Parse(command.Begin);
        var end = TimelineInstants.Parse(command.End);
        return begin is not null && end is not null && end < begin;
    }
}
