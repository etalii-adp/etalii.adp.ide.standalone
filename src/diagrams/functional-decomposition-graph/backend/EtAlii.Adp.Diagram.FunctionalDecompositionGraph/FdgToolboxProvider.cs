using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The palette: the five element types, as data, each naming the add action its drop commits.
/// </summary>
/// <remarks>
/// The drop and the placement menu run the same action, so there is no second implementation for
/// a drop to disagree with. The action ids are the client's, stated once in its <c>fdgIds.ts</c>.
/// </remarks>
public sealed class FdgToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        Item(FdgElementTypes.UiElement, "UI element", "mdi-application-outline", "A screen, or a part of one. Drop it where it belongs; its parent is a connection drawn to it."),
        Item(FdgElementTypes.Action, "Action", "mdi-gesture-tap", "Something the user does on a screen. A screen owns it."),
        Item(FdgElementTypes.DataElement, "Data element", "mdi-database-outline", "Something the application keeps. A screen, an action or other data owns it."),
        Item(FdgElementTypes.Function, "Function", "mdi-cog-outline", "Something that runs behind the screens. Anything but a Comment may own it."),
        Item(FdgElementTypes.Comment, "Comment", "mdi-note-text-outline", "A note on the graph. It connects to nothing."),
    ];

    private static ToolboxItemDefinition Item(string type, string label, string icon, string description) =>
        new($"fdg.toolbox.{type}", label, icon, description, FdgContextActionProvider.AddActionId(type));
}
