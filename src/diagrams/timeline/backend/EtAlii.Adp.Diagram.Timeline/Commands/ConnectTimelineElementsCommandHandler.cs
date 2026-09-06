using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Carries out a connect. The inverse is the disconnection of what was connected.</summary>
public sealed class ConnectTimelineElementsCommandHandler : ICommandHandler<ConnectTimelineElementsCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public ConnectTimelineElementsCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ConnectTimelineElementsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(command.From, command.To, StringComparison.Ordinal))
        {
            // Requirement 8.7. Refused with the reason rather than silently dropped.
            return Task.FromResult(CommandResult.Failure("An element cannot be connected to itself."));
        }

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing can be connected until the file is fixed."));
        }

        if (TimelineEdits.ElementOf(entry.Model, command.From) is null ||
            TimelineEdits.ElementOf(entry.Model, command.To) is null)
        {
            // A connection gesture that ends on nothing cancels with a reason (Requirement 8.5),
            // and writing a connection to an element that is not there would manufacture exactly
            // the dangling reference the validator exists to report.
            return Task.FromResult(CommandResult.Failure("Both ends of a relation must be elements of this timeline."));
        }

        // Whether the insert is about to create the connections: key, so the inverse can take
        // the key back out and the undo return the file byte for byte.
        var hadSection = TimelineWriter.HasConnectionsSection(entry.Document);

        TimelineWriter.InsertConnection(
            entry.Document, entry.Model, command.Id, command.From, command.To, command.Label);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new DisconnectTimelineConnectionCommand(
                command.BodyPath, command.Id, RemoveEmptiedConnectionsSection: !hadSection))
            : CommandResult.Failure(error));
    }
}
