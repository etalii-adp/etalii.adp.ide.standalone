using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a node where it was dropped, named after its stage: the definition's <c>add…Here</c> of that stage.</summary>
public sealed class AddSupplyChainNodeCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<AddSupplyChainNodeCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(AddSupplyChainNodeCommand command, CancellationToken cancellationToken)
    {
        return !SupplyChainNodeTypes.IsKnown(command.NodeType)
            ? CommandResult.Failure($"This notation has no `{command.NodeType}` stage.")
            : await base.RunAsync(command.NodeId.Length > 0 ? command : command with { NodeId = ShortGuid.NewShortGuid().ToString() }, cancellationToken);
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, AddSupplyChainNodeCommand command)
    {
        // The id is the host's, minted or asked for, so whether it is free is the host's to say.
        if (model.IsTaken(command.NodeId))
        {
            return EditOutcome.Refused("That id is already used in this diagram.");
        }

        var stage = SupplyChainNodeTypes.Normalize(command.NodeType);
        return SupplyChainDefinition.Run(
            document,
            model,
            $"add{char.ToUpperInvariant(stage[0])}{stage[1..]}Here",
            null,
            DislIds.Fixed(command.NodeId),
            SupplyChainDefinition.At(command.X, command.Y));
    }
}
