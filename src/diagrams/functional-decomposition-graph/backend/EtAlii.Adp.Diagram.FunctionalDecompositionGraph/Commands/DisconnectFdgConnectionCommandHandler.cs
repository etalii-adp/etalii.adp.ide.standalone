using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes one connection.</summary>
public sealed class DisconnectFdgConnectionCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<DisconnectFdgConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, DisconnectFdgConnectionCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return FdgDefinition.ConnectionOf(model.Diagram, command.ConnectionId) is { } connection
            ? FdgDefinition.Apply(body, new DislTransaction([new DislChange.Remove(connection.Id)], [], null))
            : EditOutcome.Refused("That connection is no longer in this graph.");
    }
}
