namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// The causal loop Toolbox: one entry per thing that can be added.
/// </summary>
/// <remarks>
/// Each entry names the context action it drops into, so a drop inherits that action's command,
/// its refusals and its undo without an implementation of its own - the same path the menu and
/// the keyboard take. It also means a drop cannot go where the menu would not: the action
/// provider offers "add link from here" only on a variable, so dropping a Link on empty canvas
/// is refused by the rule that keeps it out of the canvas menu, and each description says where
/// the entry goes so a user finds out before trying.
/// </remarks>
public sealed class CausalLoopToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = Diagram.CausalLoop.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new ToolboxItemDefinition(
            "causal-loop.toolbox.variable",
            "Variable",
            "mdi-circle-outline",
            "Drop on the canvas to declare a quantity the system has.",
            CausalLoopContextActionProvider.AddVariableActionId),
        new ToolboxItemDefinition(
            "causal-loop.toolbox.link",
            "Causal link",
            "mdi-arrow-right-thin",
            "Drop on a variable to state that it affects another. It arrives stating the same direction; change it to the opposite from the link's own menu.",
            CausalLoopContextActionProvider.AddLinkActionId),
        new ToolboxItemDefinition(
            "causal-loop.toolbox.loop",
            "Feedback loop",
            "mdi-sync",
            "Drop on a variable to claim the loop that runs through it. Whether it reinforces or balances is counted from the links rather than taken from the label.",
            CausalLoopContextActionProvider.AddLoopActionId),
    ];
}
