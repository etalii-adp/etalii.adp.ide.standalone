using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a flow between two nodes, refusing one to itself or one that already exists.</summary>
public sealed class ConnectSupplyChainNodesCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<ConnectSupplyChainNodesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ConnectSupplyChainNodesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.FlowId.Length > 0 ? command : command with { FlowId = ShortGuid.NewShortGuid().ToString() };

        return SupplyChainEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (SupplyChainEdits.NodeOf(model, minted.From) is null || SupplyChainEdits.NodeOf(model, minted.To) is null)
            {
                return SupplyChainEdit.Refused("A flow runs from one node to another.");
            }

            if (minted.From == minted.To)
            {
                return SupplyChainEdit.Refused("A node does not supply itself.");
            }

            if (model.Flows.Any(flow => flow.From == minted.From && flow.To == minted.To))
            {
                return SupplyChainEdit.Refused("These two are already connected by a flow in this direction.");
            }

            if (SupplyChainEdits.IsTaken(model, minted.FlowId))
            {
                return SupplyChainEdit.Refused("That id is already used in this diagram.");
            }

            return SupplyChainWriter.AddFlow(document, model, new SupplyChainFlow(
                minted.FlowId, minted.From, minted.To, Product: "", Description: "", Volume: null, Unit: "", Step: null, new LineRange(0, 0)));
        });
    }
}
