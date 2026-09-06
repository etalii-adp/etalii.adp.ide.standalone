using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The mindmap's Toolbox: one entry, a node. Dropping it on a node runs the same add-child
/// action the context menu and the Insert key run, so the drop inherits that action's
/// prompt, command and undo without an implementation of its own (tech.md's "Specifying a
/// diagram type": one implementation behind every trigger).
/// </summary>
public sealed class MindmapToolboxProvider : IDiagramToolboxProvider
{
    public DiagramOrigin Origin { get; } = Diagram.Mindmap.Origin;

    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new ToolboxItemDefinition(
            "mindmap.toolbox.node",
            "Node",
            "mdi-card-plus-outline",
            "Drop on a node to add a child under it.",
            MindmapContextActionProvider.AddChildActionId),
    ];
}
