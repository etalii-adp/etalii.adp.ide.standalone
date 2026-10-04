using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The palette: the seven stages and a group, as data, each naming the add action its drop commits.
/// </summary>
/// <remarks>
/// <b>A stage's icon is its own everywhere</b>: the palette, and the "Add … here" menu on empty
/// canvas, both read <see cref="SupplyChainStageIcons"/>.
/// </remarks>
public sealed class SupplyChainToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        Item(SupplyChainNodeTypes.Source, "Where something enters the chain: a mine, a well, a farm, a data feed."),
        Item(SupplyChainNodeTypes.Processor, "Turns what it receives into material others can use."),
        Item(SupplyChainNodeTypes.Producer, "Makes the parts or components something is built from."),
        Item(SupplyChainNodeTypes.Integrator, "Combines parts into the finished whole."),
        Item(SupplyChainNodeTypes.Hub, "Holds and moves what is finished on to where it is wanted."),
        Item(SupplyChainNodeTypes.Outlet, "Where the finished whole is offered to those who use it."),
        Item(SupplyChainNodeTypes.Consumer, "Where the chain ends: whoever uses what it delivers."),
        new(
            "supply-chain.toolbox.group",
            "Group",
            SupplyChainStageIcons.Group,
            "A frame for a region, a company or a tier; drag nodes into it.",
            SupplyChainContextActionProvider.AddGroupActionId),
    ];

    private static ToolboxItemDefinition Item(string type, string description) =>
        new($"supply-chain.toolbox.{type}", SupplyChainNodeTypes.Display(type), SupplyChainStageIcons.Of(type), description, SupplyChainContextActionProvider.AddActionId(type));
}

/// <summary>The icon each stage, and a group, is shown with wherever it is offered.</summary>
public static class SupplyChainStageIcons
{
    /// <summary>A group's icon.</summary>
    public const string Group = "mdi-group";

    /// <summary>The icon of <paramref name="stage"/>, or a plain plus for a stage this module does not know.</summary>
    public static string Of(string stage) => SupplyChainNodeTypes.Normalize(stage) switch
    {
        SupplyChainNodeTypes.Source => "mdi-tray-arrow-up",
        SupplyChainNodeTypes.Processor => "mdi-cog-outline",
        SupplyChainNodeTypes.Producer => "mdi-factory",
        SupplyChainNodeTypes.Integrator => "mdi-puzzle-outline",
        SupplyChainNodeTypes.Hub => "mdi-hub-outline",
        SupplyChainNodeTypes.Outlet => "mdi-storefront-outline",
        SupplyChainNodeTypes.Consumer => "mdi-account-outline",
        _ => "mdi-plus",
    };
}
