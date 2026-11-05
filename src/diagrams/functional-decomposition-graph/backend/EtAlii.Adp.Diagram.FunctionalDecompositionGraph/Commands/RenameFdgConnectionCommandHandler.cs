using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a connection's name.</summary>
public sealed class RenameFdgConnectionCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<RenameFdgConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(FdgBody body, DislModel model, RenameFdgConnectionCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return FdgDefinition.ConnectionOf(model.Diagram, command.ConnectionId) is { } connection
            ? FdgDefinition.Set(body, connection, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = command.Name })
            : EditOutcome.Refused("That connection is no longer in this graph.");
    }
}
