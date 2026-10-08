using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The palette: a Trend, a Trigger and a Note, described as data, each dropping its add action
/// (Requirement 11.1, and ghg-triggers-and-notes Requirement 7.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived from the DISL definition</b> (<see cref="GhgDefinition.Toolbox"/>): its toolbox group's
/// tools, their ids and drops from its <c>x-ghg</c> block.
/// </para>
/// <para>
/// Influences are drawn, not dropped, so the palette has no item for one. The drop and the placement
/// menu run the same action, so there is no second implementation for a drop to disagree with.
/// </para>
/// </remarks>
public sealed class GhgToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items => GhgDefinition.Toolbox;
}
