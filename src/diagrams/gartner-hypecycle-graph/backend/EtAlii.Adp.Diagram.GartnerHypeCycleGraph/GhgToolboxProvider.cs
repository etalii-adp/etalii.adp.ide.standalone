using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The palette: one Trend, described as data, whose drop commits the add action (Requirement 11.1).
/// </summary>
/// <remarks>
/// Influences are drawn, not dropped, so the palette has one item. The drop and the placement menu run
/// the same action, so there is no second implementation for a drop to disagree with.
/// </remarks>
public sealed class GhgToolboxProvider : IDiagramToolboxProvider
{
    /// <summary>The Trend item's id.</summary>
    public const string TrendItemId = "ghg.toolbox.trend";

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            TrendItemId,
            "Trend",
            "mdi-arrow-right-bold-box-outline",
            "A trend through the hype cycle. Drop it where it starts; it is a year long with all four phases.",
            GhgContextActionProvider.AddTrendActionId),
    ];
}
