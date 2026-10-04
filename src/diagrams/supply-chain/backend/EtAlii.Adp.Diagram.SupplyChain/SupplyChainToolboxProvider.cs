using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>The palette: the seven stages, as data, each naming the add action its drop commits.</summary>
public sealed class SupplyChainToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        Item(SupplyChainNodeTypes.RawMaterial, "mdi-pickaxe", "Where it starts: a mine, a well, a farm."),
        Item(SupplyChainNodeTypes.Supplier, "mdi-flask-outline", "Refines, processes or makes parts and materials for others."),
        Item(SupplyChainNodeTypes.Manufacturer, "mdi-factory", "Makes the components a product is built from."),
        Item(SupplyChainNodeTypes.Assembler, "mdi-cog-transfer-outline", "Builds the finished product from components."),
        Item(SupplyChainNodeTypes.Distributor, "mdi-truck-outline", "Stores and moves finished goods to where they are sold."),
        Item(SupplyChainNodeTypes.Retailer, "mdi-storefront-outline", "Sells to the people and businesses who use the product."),
        Item(SupplyChainNodeTypes.Consumer, "mdi-account-group-outline", "Where the chain ends: who buys and uses it."),
    ];

    private static ToolboxItemDefinition Item(string type, string icon, string description) =>
        new($"supply-chain.toolbox.{type}", SupplyChainNodeTypes.Display(type), icon, description, SupplyChainContextActionProvider.AddActionId(type));
}
