namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// An operation of <c>behavior.operations</c> (DISL §9.3), typed at load: what it is for, when it is unavailable,
/// and what it does - a <c>plugin</c> handed back whole, or its actions.
/// </summary>
/// <param name="Id">The operation's name in <c>behavior.operations</c>.</param>
/// <param name="For">Its <c>for</c>: the types, or <c>diagram</c> or <c>selection</c>, it applies to; empty for any.</param>
/// <param name="Unavailable">Its <c>unavailable</c> Reasons, in order; none when it states none.</param>
/// <param name="Enabled">Its <c>enabled</c> condition, or null when it has none.</param>
/// <param name="Plugin">Its <c>plugin</c>, or null when it runs actions.</param>
/// <param name="Actions">Its <c>actions</c>; an empty list when it states none.</param>
/// <param name="Label">Its <c>label</c>, which a context menu entry running it is named by; null when it states none.</param>
/// <param name="Icon">Its <c>icon</c>, a context menu entry's when the entry states none.</param>
/// <param name="Shortcut">Its <c>shortcut</c>, a context menu entry's when the entry states none.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime read an operation that is not an object, null for one that
/// is; such an operation still throws it where its <c>unavailable</c> was read.
/// </param>
internal sealed record DislOperation(
    string Id,
    IReadOnlyList<string> For,
    IReadOnlyList<DislReason> Unavailable,
    DislExpressionField? Enabled,
    DislOperationPlugin? Plugin,
    DislActionList Actions,
    DislMessage? Label,
    string? Icon,
    string? Shortcut,
    string? NotAnObject)
{
    /// <summary>
    /// Whether the operation is for a target that is of a type when <paramref name="isA"/> says so, or the diagram when
    /// it is null: its <c>for</c> is empty, or names the diagram, the selection, or one of the target's types.
    /// </summary>
    public bool AppliesTo(Func<string, bool>? isA) =>
        For.Count == 0 || For.Contains("diagram") || For.Contains("selection") || isA is not null && For.Any(isA);
}

/// <summary>
/// An operation's <c>plugin</c> (DISL §9.3), handed back whole: the plugin's name and its <c>args</c> as written, since a
/// PluginCall's arguments are values the plugin's declaration validates, not expressions.
/// </summary>
internal sealed record DislOperationPlugin(string Name, IReadOnlyList<(string Name, DislActionValue.Literal Value)> Arguments);

/// <summary>
/// The operations of a specification (DISL §9.3) and the <c>behavior.editGate</c> every one of them passes first, typed
/// at load.
/// </summary>
/// <param name="Operations">The operations, by name.</param>
/// <param name="EditGate">The edit gate's Reasons, in order; none when there is no gate.</param>
/// <param name="Malformed">
/// The message System.Text.Json threw when the runtime looked for an operation in a <c>behavior</c> or
/// <c>behavior.operations</c> that is not an object; such a specification still throws it on every look-up.
/// </param>
internal sealed record DislOperations(IReadOnlyDictionary<string, DislOperation> Operations, IReadOnlyList<DislReason> EditGate, string? Malformed)
{
    /// <summary>The operation <paramref name="id"/>, or null when there is none.</summary>
    public DislOperation? Find(string id) =>
        Malformed is not null ? throw new InvalidOperationException(Malformed) : Operations.GetValueOrDefault(id);
}
