namespace EtAlii.Adp.Diagram;

/// <summary>
/// One draggable Toolbox entry a diagram type contributes, described as data - the client
/// renders a palette it does not understand, exactly as it renders context actions.
/// </summary>
/// <param name="Id">Stable identifier of the entry itself, unique within its diagram type.</param>
/// <param name="Label">What the palette shows.</param>
/// <param name="Icon">An @mdi/font class, e.g. <c>mdi-card-plus-outline</c>.</param>
/// <param name="Description">The entry's tooltip/hint - what dropping it does.</param>
/// <param name="DropActionId">
/// The context action executed against the element the entry is dropped on. Reusing the
/// action id keeps one implementation - command, prompt, undo - behind the drop, the menu
/// and the keyboard alike.
/// </param>
public sealed record ToolboxItemDefinition(
    string Id,
    string Label,
    string Icon,
    string Description,
    string DropActionId);
