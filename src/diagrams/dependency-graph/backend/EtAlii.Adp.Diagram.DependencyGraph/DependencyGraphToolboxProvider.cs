using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The palette: one entry, a node, carrying only data and naming the add action its drop commits.
/// There is no second implementation for a drop to disagree with - the drop and the menu run the
/// same command.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived from the DISL definition</b> (<see cref="DependencyGraphDefinition.Toolbox"/>): its toolbox
/// group's one tool, its id and drop from its <c>x-dependencies</c> block.
/// </para>
/// <para>
/// The timeline offered two, an element and a Moment. A moment is a point in time and there are
/// none here, so the entry is deleted rather than renamed into something this type does not have.
/// </para>
/// </remarks>
public sealed class DependencyGraphToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items => DependencyGraphDefinition.Toolbox;
}
