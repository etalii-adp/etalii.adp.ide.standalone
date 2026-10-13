using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a flow between two nodes, refusing one to itself or one that already exists.</summary>
public sealed class ConnectSupplyChainNodesCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<ConnectSupplyChainNodesCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(ConnectSupplyChainNodesCommand command) =>
        base.Execute(command.FlowId.Length > 0 ? command : command with { FlowId = ShortGuid.NewShortGuid().ToString() });

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, ConnectSupplyChainNodesCommand command)
    {
        if (model.NodeOf(command.From) is null || model.NodeOf(command.To) is null)
        {
            return EditOutcome.Refused("A flow runs from one node to another.");
        }

        if (command.From == command.To)
        {
            return EditOutcome.Refused("A node does not supply itself.");
        }

        if (model.Flows.Any(flow => flow.From == command.From && flow.To == command.To))
        {
            return EditOutcome.Refused("These two are already connected by a flow in this direction.");
        }

        if (model.IsTaken(command.FlowId))
        {
            return EditOutcome.Refused("That id is already used in this diagram.");
        }

        return SupplyChainWriter.AddFlow(document, model, new SupplyChainFlow(
            command.FlowId, command.From, command.To, Product: "", Description: "", Volume: null, Unit: "", Step: null, new LineRange(0, 0)));
    }
}
