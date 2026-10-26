using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Removes whatever the id names, as the definition's deletion policy says: a node with its flows, a group from its members.</summary>
public sealed class RemoveSupplyChainEntryCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<RemoveSupplyChainEntryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, RemoveSupplyChainEntryCommand command) =>
        SupplyChainDefinition.Remove(document, model, command.EntryId) ?? Gone;
}
