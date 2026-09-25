using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a connection's name.</summary>
public sealed class RenameFdgConnectionCommandHandler(IFdgDocumentStore documents) : ICommandHandler<RenameFdgConnectionCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameFdgConnectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            FdgEdits.ConnectionOf(model, command.ConnectionId) is { } connection
                ? FdgWriter.SetConnectionName(document, connection, command.Name)
                : FdgEdit.Refused("That connection is no longer in this graph."));
    }
}
