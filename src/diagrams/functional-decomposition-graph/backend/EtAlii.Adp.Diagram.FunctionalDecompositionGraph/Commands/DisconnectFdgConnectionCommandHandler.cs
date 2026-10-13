using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes one connection.</summary>
public sealed class DisconnectFdgConnectionCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<DisconnectFdgConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, DisconnectFdgConnectionCommand command) =>
        model.ConnectionOf(command.ConnectionId) is { } connection
            ? FdgWriter.Disconnect(document, connection)
            : EditOutcome.Refused("That connection is no longer in this graph.");
}
