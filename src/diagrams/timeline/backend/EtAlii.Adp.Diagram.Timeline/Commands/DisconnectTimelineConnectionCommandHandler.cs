using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a disconnect. The inverse is a connect carrying the same id, endpoints and
/// label - sufficient because a connection block is machine-shaped; an element's block, which
/// can hold an author's comments, gets the byte-exact restore instead.
/// </summary>
public sealed class DisconnectTimelineConnectionCommandHandler : ICommandHandler<DisconnectTimelineConnectionCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public DisconnectTimelineConnectionCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(DisconnectTimelineConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be disconnected until the file is fixed."));
        }

        var connection = TimelineEdits.ConnectionOf(entry.Model, command.ConnectionId);
        if (connection is null)
        {
            return Task.FromResult(CommandResult.Failure("That relation is no longer in this timeline."));
        }

        var inverse = new ConnectTimelineElementsCommand(
            command.BodyPath, connection.Id, connection.From, connection.To, connection.Label);

        TimelineWriter.RemoveConnection(entry.Document, connection);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(error));
    }
}
