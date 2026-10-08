namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// One model change an action list made (DISL §9.4), in the metamodel's terms: what a host writes,
/// through <see cref="DislWrite"/> for an FBL body. Values are CEL values, as the actions computed them.
/// </summary>
public abstract record DislChange
{
    /// <summary>A node of <paramref name="Type"/> created with <paramref name="Id"/> (<c>create</c>).</summary>
    /// <param name="At">The domain point it was created at, when the action said one: placement is the host's.</param>
    public sealed record Create(string Type, string Id, IReadOnlyDictionary<string, object?> Attributes, string? ParentId, object? At) : DislChange;

    /// <summary>
    /// A relation of <paramref name="Type"/> created with <paramref name="Id"/> from <paramref name="SourceId"/> to
    /// <paramref name="TargetId"/> (<c>connect</c>), with the attributes its action gave.
    /// </summary>
    public sealed record Connect(string Type, string Id, string SourceId, string TargetId, IReadOnlyDictionary<string, object?> Attributes) : DislChange;

    /// <summary>Attributes of an element of <paramref name="Type"/> assigned (<c>set</c>), or forgotten where the value is null (<c>unset</c>).</summary>
    public sealed record Set(string ElementId, string Type, IReadOnlyDictionary<string, object?> Attributes) : DislChange;

    /// <summary>An element deleted (<c>delete</c>, or a deletion policy's cascade).</summary>
    public sealed record Remove(string ElementId) : DislChange;

    /// <summary>A node moved under another parent, or to the top level (<c>reparent</c>).</summary>
    /// <param name="Index">
    /// Where among the new parent's children it went, counted before the move with the node itself
    /// included; negative for last.
    /// </param>
    public sealed record Reparent(string ElementId, string? ParentId, int Index = -1) : DislChange;

    /// <summary>
    /// A node's type changed to <paramref name="Type"/> (<c>behavior.retype</c>, §9.5), with the values its
    /// <c>attributeMapping</c> gives; the attributes both types declare are kept by whoever writes it.
    /// </summary>
    public sealed record Retype(string ElementId, string Type, IReadOnlyDictionary<string, object?> Attributes) : DislChange;
}

/// <summary>
/// An action the model does not hold and the interpreter hands back to the host (DISL §9.4): a layout
/// to run, a plugin action to call, a selection to make, an inline edit to open.
/// </summary>
public abstract record HostAction
{
    /// <summary><c>layout</c>: run <paramref name="Algorithm"/> (section 10) over the diagram.</summary>
    public sealed record Layout(string Algorithm) : HostAction;

    /// <summary><c>plugin</c>: the plugin action <paramref name="Name"/> with its evaluated arguments.</summary>
    public sealed record Plugin(string Name, IReadOnlyDictionary<string, object?> Arguments) : HostAction;

    /// <summary><c>select</c>: the ids of what is to be selected.</summary>
    public sealed record Select(IReadOnlyList<string> ElementIds) : HostAction;

    /// <summary><c>editLabel</c>: open the inline editor on <paramref name="ElementId"/>'s label <paramref name="Label"/>, or its first.</summary>
    public sealed record EditLabel(string ElementId, string? Label) : HostAction;
}

/// <summary>
/// What running an action list gave: the model changes in order, the actions handed back, and the
/// refusal that rolled the transaction back, when one did (<c>abort</c>, an unavailable operation, an
/// action this runtime cannot run, or an expression that failed).
/// </summary>
public sealed record DislTransaction(IReadOnlyList<DislChange> Changes, IReadOnlyList<HostAction> HostActions, string? Refusal)
{
    /// <summary>Whether the transaction stands.</summary>
    public bool WasApplied => Refusal is null;

    /// <summary>A transaction refused with <paramref name="reason"/>, which changes nothing.</summary>
    public static DislTransaction Refused(string reason) => new([], [], reason);
}
