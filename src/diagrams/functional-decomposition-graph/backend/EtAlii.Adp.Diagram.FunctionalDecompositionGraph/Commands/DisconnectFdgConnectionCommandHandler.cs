using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes one connection.</summary>
public sealed class DisconnectFdgConnectionCommandHandler(IFdgDocumentStore documents) : ICommandHandler<DisconnectFdgConnectionCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(DisconnectFdgConnectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            FdgEdits.ConnectionOf(model, command.ConnectionId) is { } connection
                ? FdgWriter.Disconnect(document, connection)
                : FdgEdit.Refused("That connection is no longer in this graph."));
    }
}
