using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a node where it was dropped, named after its stage.</summary>
public sealed class AddSupplyChainNodeCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<AddSupplyChainNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddSupplyChainNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!SupplyChainNodeTypes.IsKnown(command.NodeType))
        {
            return Task.FromResult(CommandResult.Failure($"This notation has no `{command.NodeType}` stage."));
        }

        var minted = command.NodeId.Length > 0 ? command : command with { NodeId = ShortGuid.NewShortGuid().ToString() };

        return SupplyChainEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (SupplyChainEdits.IsTaken(model, minted.NodeId))
            {
                return SupplyChainEdit.Refused("That id is already used in this diagram.");
            }

            // The drop is the centre; the document holds the top-left.
            var node = new SupplyChainNode(
                minted.NodeId,
                SupplyChainNodeTypes.Normalize(minted.NodeType),
                SupplyChainEdits.Unique(model.Nodes.Select(existing => existing.Name), $"New {SupplyChainNodeTypes.Display(minted.NodeType).ToLowerInvariant()}"),
                Description: "",
                Group: "",
                Quantity: 0,
                Unit: "",
                Step: null,
                X: minted.X - (SupplyChainGeometry.NodeWidth / 2),
                Y: minted.Y - (SupplyChainGeometry.NodeHeight / 2),
                Range: new LineRange(0, 0));

            return SupplyChainWriter.AddNode(document, model, node);
        });
    }
}
