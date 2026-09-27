using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a relabel. The inverse is the same command carrying the previous label.</summary>
public sealed class RelabelTimelineConnectionCommandHandler : ICommandHandler<RelabelTimelineConnectionCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RelabelTimelineConnectionCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RelabelTimelineConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be relabelled until the file is fixed."));
        }

        var connection = TimelineEdits.ConnectionOf(entry.Model, command.ConnectionId);
        if (connection is null)
        {
            return Task.FromResult(CommandResult.Failure("That relation is no longer in this timeline."));
        }

        var inverse = new RelabelTimelineConnectionCommand(command.BodyPath, command.ConnectionId, connection.Label);
        TimelineWriter.SetConnectionLabel(entry.Document, connection, command.Label);

        var saved = _documents.Save(command.BodyPath, entry);
        return Task.FromResult(!saved.Failed
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(saved.Error));
    }
}
