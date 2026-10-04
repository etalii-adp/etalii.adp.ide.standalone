using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a create-and-relate. The inverse is the removal of the new element, whose
/// relations - including the one made here - go with it.
/// </summary>
public sealed class AddConnectedTimelineElementCommandHandler : ICommandHandler<AddConnectedTimelineElementCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public AddConnectedTimelineElementCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddConnectedTimelineElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be added until the file is fixed."));
        }

        if (TimelineEdits.ElementOf(entry.Model, command.FromElementId) is null)
        {
            return Task.FromResult(CommandResult.Failure("The element this relation starts from is no longer in this timeline."));
        }

        if (TimelineEdits.ElementOf(entry.Model, command.NewElementId) is not null)
        {
            // Redo lands here when the element survived - re-adding would duplicate the id.
            return Task.FromResult(CommandResult.Failure("An element with that id is already in this timeline."));
        }

        TimelineWriter.InsertElement(
            entry.Document, entry.Model, command.NewElementId, "New element", command.Begin, command.End, command.Row);

        // The insert moved lines, so the relation is spliced against a fresh parse rather than
        // the ranges the first model recorded.
        var reparsed = TimelineParser.Parse(entry.Document);
        // Whether the insert is about to create the connections: key, so the inverse can take
        // the key back out and the undo return the file byte for byte.
        var hadSection = TimelineWriter.HasConnectionsSection(entry.Document);

        // A begin-anchor gesture runs the relation the other way: out of the new element's end
        // and into the existing one's start.
        (string source, string target) = command.NewElementIsSource
            ? (command.NewElementId, command.FromElementId)
            : (command.FromElementId, command.NewElementId);
        TimelineWriter.InsertConnection(
            entry.Document, reparsed, command.RelationId, source, target, "");

        var saved = _documents.Save(command.BodyPath, entry);
        return Task.FromResult(!saved.Failed
            ? CommandResult.Success(new RemoveTimelineElementCommand(
                command.BodyPath, command.NewElementId, RemoveEmptiedConnectionsSection: !hadSection))
            : CommandResult.Failure(saved.Error));
    }
}
