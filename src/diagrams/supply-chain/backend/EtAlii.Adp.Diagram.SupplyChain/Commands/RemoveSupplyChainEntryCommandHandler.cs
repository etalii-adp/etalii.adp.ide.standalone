using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Removes whatever the id names.</summary>
public sealed class RemoveSupplyChainEntryCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<RemoveSupplyChainEntryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, RemoveSupplyChainEntryCommand command)
    {
        if (model.NodeOf(command.EntryId) is { } node)
        {
            return SupplyChainWriter.RemoveNode(document, model, node);
        }

        if (model.FlowOf(command.EntryId) is { } flow)
        {
            return SupplyChainWriter.Remove(document, flow.Range);
        }

        return model.GroupOf(command.EntryId) is { } group
            ? SupplyChainWriter.RemoveGroup(document, model, group)
            : Gone;
    }
}
