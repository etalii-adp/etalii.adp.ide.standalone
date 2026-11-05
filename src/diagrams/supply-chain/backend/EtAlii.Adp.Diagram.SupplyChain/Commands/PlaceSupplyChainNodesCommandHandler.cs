using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Writes the placements, in one edit and so in one undo.</summary>
public sealed class PlaceSupplyChainNodesCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<PlaceSupplyChainNodesCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, PlaceSupplyChainNodesCommand command) =>
        SupplyChainDefinition.Place(document, model, command.Places);
}
