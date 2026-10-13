using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a connection's name.</summary>
public sealed class RenameFdgConnectionCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<RenameFdgConnectionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, RenameFdgConnectionCommand command) =>
        model.ConnectionOf(command.ConnectionId) is { } connection
            ? FdgWriter.SetConnectionName(document, connection, command.Name)
            : EditOutcome.Refused("That connection is no longer in this graph.");
}
