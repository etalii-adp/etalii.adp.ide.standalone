using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The mindmap's Toolbox: one entry, a node. Dropping it on a node runs the same add-child
/// action the context menu and the Insert key run, so the drop inherits that action's
/// prompt, command and undo without an implementation of its own (tech.md's "Specifying a
/// diagram type": one implementation behind every trigger).
/// </summary>
/// <remarks>
/// <b>Derived from the DISL definition</b> (<see cref="MindmapDefinition.Toolbox"/>): its toolbox
/// group's one tool, its id and its drop from the <c>x-mindmap</c> block.
/// </remarks>
public sealed class MindmapToolboxProvider : IDiagramToolboxProvider
{
    public DiagramOrigin Origin { get; } = Diagram.Mindmap.Origin;

    public IReadOnlyList<ToolboxItemDefinition> Items => MindmapDefinition.Toolbox;
}
