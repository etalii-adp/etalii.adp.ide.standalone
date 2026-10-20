namespace EtAlii.Adp.Specification.Disl;

/// <summary>An action list (DISL §9.4), typed at load: its actions in order; none for a value that is not a list.</summary>
internal sealed record DislActionList(IReadOnlyList<DislAction> Actions)
{
    public static DislActionList Empty { get; } = new([]);
}

/// <summary>
/// One action of an action list (DISL §9.4), typed at load: a case for each kind <see cref="ActionRunner"/> runs, and
/// the cases it refuses - an item that is not an object, an action of no kind it knows, a kind it cannot run yet.
/// </summary>
/// <param name="At">Where the action is, as a refusal names it.</param>
/// <param name="When">The action's <c>when</c>, or null when it has none: while it does not hold, the action is skipped.</param>
/// <remarks>
/// <para>
/// <b>A kind is the first of <see cref="DislActionReader"/>'s kinds the action carries</b>, as the runtime always chose
/// it. Every value position is a <see cref="DislActionValue"/>, an Expression's with the program compiled at load; a
/// member that is absent is null, so the runner gives its own fallback.
/// </para>
/// <para>
/// <b>A body of the wrong shape</b> is kept as the runtime read it: a <c>set</c>, <c>let</c> or <c>view</c> whose body
/// is not an object, and a <c>plugin</c> whose name is not a string, carry the message System.Text.Json threw when the
/// runtime read them, and still throw it at the moment it did.
/// </para>
/// </remarks>
internal abstract record DislAction(string At, DislExpressionField? When)
{
    /// <summary>An item of the list that is not an object; refused before its <c>when</c> would be read.</summary>
    public sealed record NotAnObject(string At) : DislAction(At, null);

    /// <summary>An object of no kind this runtime knows.</summary>
    public sealed record Unknown(string At, DislExpressionField? When) : DislAction(At, When);

    /// <summary>A kind this runtime knows but cannot run yet, such as <c>move</c> or <c>notify</c>.</summary>
    public sealed record Unsupported(string At, DislExpressionField? When, string Kind) : DislAction(At, When);

    /// <summary>A <c>set</c>: values for attributes of its <c>target</c>, by default <c>self</c>.</summary>
    public sealed record Set(string At, DislExpressionField? When, DislActionValue? Target, IReadOnlyList<(string Name, DislActionValue Value)> Values, string? Malformed)
        : DislAction(At, When);

    /// <summary>An <c>unset</c>: attributes of its <c>target</c>, by default <c>self</c>, forgotten.</summary>
    public sealed record Unset(string At, DislExpressionField? When, DislActionValue? Target, IReadOnlyList<string> Names) : DislAction(At, When);

    /// <summary>A <c>create</c>: a node of its <c>type</c>, with its <c>attributes</c>, under its <c>parent</c> and <c>slot</c>, <c>at</c> a point; <c>as</c> names it.</summary>
    public sealed record Create(
        string At,
        DislExpressionField? When,
        string BodyAt,
        DislActionValue? Type,
        DislActionValue? Parent,
        DislActionValue? Slot,
        DislActionValue? Point,
        IReadOnlyList<(string Name, DislActionValue Value)> Attributes,
        string? As) : DislAction(At, When);

    /// <summary>A <c>connect</c>: a relation of its <c>type</c> from its <c>source</c> to its <c>target</c>, with its <c>attributes</c>; <c>as</c> names it.</summary>
    public sealed record Connect(
        string At,
        DislExpressionField? When,
        string BodyAt,
        DislActionValue? Type,
        DislActionValue? Source,
        DislActionValue? Target,
        IReadOnlyList<(string Name, DislActionValue Value)> Attributes,
        string? As) : DislAction(At, When);

    /// <summary>A <c>retype</c>: its <c>target</c>, by default <c>self</c>, made the type <c>to</c> names.</summary>
    public sealed record Retype(string At, DislExpressionField? When, string BodyAt, DislActionValue? Target, DislActionValue? To) : DislAction(At, When);

    /// <summary>A <c>delete</c> of the elements its value gives.</summary>
    public sealed record Delete(string At, DislExpressionField? When, DislActionValue Elements) : DislAction(At, When);

    /// <summary>A <c>reparent</c>: its <c>target</c>, by default <c>self</c>, moved under its <c>parent</c> and <c>slot</c>, <c>after</c> or <c>before</c> a sibling.</summary>
    public sealed record Reparent(
        string At,
        DislExpressionField? When,
        DislActionValue? Target,
        DislActionValue? Parent,
        DislActionValue? Slot,
        DislActionValue? After,
        DislActionValue? Before) : DislAction(At, When);

    /// <summary>A <c>let</c>: names bound, in order, to values the actions after it read.</summary>
    public sealed record Let(string At, DislExpressionField? When, IReadOnlyList<(string Name, DislActionValue Value)> Bindings, string? Malformed) : DislAction(At, When);

    /// <summary>An <c>if</c>: its <c>then</c> while its condition holds, else its <c>else</c>; an empty list where the action has none.</summary>
    public sealed record If(string At, DislExpressionField? When, DislExpressionField Condition, DislActionList Then, DislActionList Else) : DislAction(At, When);

    /// <summary>A <c>forEach</c>: its <c>do</c> for each item of its list, the item bound <c>as</c> its name (<c>item</c> by default).</summary>
    public sealed record ForEach(string At, DislExpressionField? When, DislActionValue Items, string As, DislActionList Do) : DislAction(At, When);

    /// <summary>A <c>select</c> of the elements its value gives, handed to the host.</summary>
    public sealed record Select(string At, DislExpressionField? When, DislActionValue Elements) : DislAction(At, When);

    /// <summary>An <c>editLabel</c> of its <c>target</c>, by default <c>self</c>, on its <c>label</c>, handed to the host.</summary>
    public sealed record EditLabel(string At, DislExpressionField? When, DislActionValue? Target, string? Label) : DislAction(At, When);

    /// <summary>A <c>layout</c> by its <c>algorithm</c>, handed to the host.</summary>
    public sealed record Layout(string At, DislExpressionField? When, string Algorithm) : DislAction(At, When);

    /// <summary>A <c>view</c> of its <c>target</c>, by default <c>self</c>: its values, or for <c>filters</c> and <c>groups</c> a map of them, handed to the host.</summary>
    public sealed record View(string At, DislExpressionField? When, DislActionValue? Target, IReadOnlyList<DislViewValue> Values, string? Malformed) : DislAction(At, When);

    /// <summary>A <c>plugin</c> call of the plugin it names with its <c>args</c>, handed to the host.</summary>
    public sealed record Plugin(string At, DislExpressionField? When, IReadOnlyList<(string Name, DislActionValue Value)> Arguments, string Name, string? Malformed) : DislAction(At, When);

    /// <summary>An <c>abort</c>: the transaction refused with its <c>message</c>, or the runtime's own.</summary>
    public sealed record Abort(string At, DislExpressionField? When, DislActionValue? Message) : DislAction(At, When);
}

/// <summary>One property of a <c>view</c> action: one value, or for <c>filters</c> and <c>groups</c> a map of values by filter id or group key.</summary>
internal sealed record DislViewValue(string Name, DislActionValue? Value, IReadOnlyList<(string Key, DislActionValue Value)>? Map);
