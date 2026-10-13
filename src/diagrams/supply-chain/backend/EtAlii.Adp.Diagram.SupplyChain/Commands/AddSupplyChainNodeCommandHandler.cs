using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a node where it was dropped, named after its stage.</summary>
public sealed class AddSupplyChainNodeCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<AddSupplyChainNodeCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(AddSupplyChainNodeCommand command)
    {
        return !SupplyChainNodeTypes.IsKnown(command.NodeType)
            ? CommandResult.Failure($"This notation has no `{command.NodeType}` stage.")
            : base.Execute(command.NodeId.Length > 0 ? command : command with { NodeId = ShortGuid.NewShortGuid().ToString() });
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SupplyChainModel model, AddSupplyChainNodeCommand command)
    {
        if (model.IsTaken(command.NodeId))
        {
            return EditOutcome.Refused("That id is already used in this diagram.");
        }

        // The drop is the centre; the document holds the top-left.
        var node = new SupplyChainNode(
            command.NodeId,
            SupplyChainNodeTypes.Normalize(command.NodeType),
            UniqueName.Of(model.Nodes.Select(existing => existing.Name), $"New {SupplyChainNodeTypes.Display(command.NodeType).ToLowerInvariant()}"),
            Description: "",
            Group: "",
            Quantity: 0,
            Unit: "",
            Step: null,
            X: command.X - (SupplyChainGeometry.NodeWidth / 2),
            Y: command.Y - (SupplyChainGeometry.NodeHeight / 2),
            Range: new LineRange(0, 0));

        return SupplyChainWriter.AddNode(document, model, node);
    }
}
