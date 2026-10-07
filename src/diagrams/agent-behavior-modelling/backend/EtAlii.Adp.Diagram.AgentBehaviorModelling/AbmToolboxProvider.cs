using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>The palette: the eleven kinds of node, each naming the add action its drop commits.</summary>
/// <remarks>
/// <para>
/// <b>Derived from the DISL definition</b> (<see cref="AbmDefinition.Toolbox"/>): its toolbox group's
/// tools, their ids and drops from its <c>x-abm</c> block.
/// </para>
/// <para>The drop and the placement menu run the same action, so there is no second implementation to disagree with.</para>
/// </remarks>
public sealed class AbmToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items => AbmDefinition.Toolbox;
}
