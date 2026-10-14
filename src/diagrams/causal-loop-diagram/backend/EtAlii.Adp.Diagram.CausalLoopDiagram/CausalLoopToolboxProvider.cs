using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The causal loop Toolbox: one entry per thing that can be added, derived from the DISL definition's
/// toolbox with the wire ids of its <c>x-cld</c> block (causal-loop-disl-fbl Requirement 3.1).
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
    public IReadOnlyList<ToolboxItemDefinition> Items => CausalLoopDefinition.Toolbox;
}
