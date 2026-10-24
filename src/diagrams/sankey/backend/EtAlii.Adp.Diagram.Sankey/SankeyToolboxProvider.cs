namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>The palette, derived from the DISL definition (<see cref="SankeyDefinition.Toolbox"/>): one node, naming the add action its drop commits.</summary>
/// <remarks>
/// One entry, because a Sankey diagram has one kind of element: what a node stands for is the
/// document's business, so the toolbox offers a node and the reader names it.
/// </remarks>
public sealed class SankeyToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public ToolOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items => SankeyDefinition.Toolbox;
}
