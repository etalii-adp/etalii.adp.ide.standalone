using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>The palette: one node, as data, naming the add action its drop commits.</summary>
/// <remarks>
/// One entry, because a Sankey diagram has one kind of element: what a node stands for is the
/// document's business, so the toolbox offers a node and the reader names it.
/// </remarks>
public sealed class SankeyToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new("sankey.toolbox.node", "Node", "mdi-chart-sankey", "Something a quantity flows into, out of, or through. Drop it in the column it belongs to.", SankeyContextActionProvider.AddActionId),
    ];
}
