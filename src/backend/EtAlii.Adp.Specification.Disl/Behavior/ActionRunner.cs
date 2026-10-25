using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Runs one action list (DISL §9.4) against a working diagram: in order, each action's expressions
/// seeing every earlier action's effect, every action skipped while its <c>when</c> does not hold.
/// </summary>
/// <remarks>
/// <para>
/// <b>The diagram it is given is the transaction's working state, and it is changed as the actions
/// run</b>: a <c>set</c> stores its values, a <c>create</c> adds its node, a <c>delete</c> takes the
/// element out. A host gives it a diagram of its own for the transaction, such as one read from a
/// copy of the body, and drops it when the transaction is refused.
/// </para>
/// <para>
/// <b>Model actions become <see cref="DislChange"/>s</b>: <c>set</c>, <c>unset</c>, <c>create</c>,
/// <c>connect</c>, <c>retype</c>, <c>delete</c> and <c>reparent</c>. <c>let</c>, <c>if</c> and <c>forEach</c> steer the run.
/// <c>layout</c>, <c>plugin</c>, <c>select</c>, <c>editLabel</c> and <c>view</c> are handed back as
/// <see cref="HostAction"/>s. <c>abort</c> refuses the transaction with its message. Any other action,
/// and an expression that fails, refuses it too: a runtime that cannot do what a definition says does
/// not do part of it.
/// </para>
/// <para>
/// <b>It runs the actions as they were typed at load</b> (<see cref="DislActionReader"/>): each value position holds
/// the program the loader compiled for it; a position the loader did not compile is refused at load.
/// </para>
/// <para>
/// <b>A <c>reparent</c> is applied like the others</b>: the node moves, with everything beneath it,
/// to its new parent at the place its <c>after</c> or <c>before</c> names, or last, and the change
/// records that place as an index among the new parent's children counted before the move.
/// </para>
/// </remarks>
internal sealed class ActionRunner(DislSpecification specification, DislDiagram diagram, IIdSource ids)
{
    private readonly List<DislChange> _changes = [];
    private readonly List<HostAction> _host = [];

    public IReadOnlyList<DislChange> Changes => _changes;

    /// <summary>
    /// When set, each element as it was before the first <c>set</c> or <c>unset</c> on it, which a
    /// change's further hooks read as <c>old</c> (<see cref="HookRunner"/>).
    /// </summary>
    public Dictionary<DislElement, DislElement>? Before { get; set; }

    /// <summary>
    /// Whether the values set are typed by the user, in a form or an inline label (DISL 0.3 §4.3): a number past its
    /// attribute's <c>min</c> or <c>max</c> is then refused, or stopped at the bound under <c>outOfRange.typed: "clamp"</c>.
    /// </summary>
    public bool Typed { get; init; }

    /// <summary>Why the transaction was refused, or null while it stands.</summary>
    private string? Refusal { get; set; }

    /// <summary>The transaction so far.</summary>
    public DislTransaction Transaction => Refusal is { } refusal ? DislTransaction.Refused(refusal) : new DislTransaction([.. _changes], [.. _host], null);

    /// <summary>Runs <paramref name="actions"/>; false once the transaction is refused.</summary>
    public bool Run(DislActionList actions, Dictionary<string, object?> variables)
    {
        foreach (var action in actions.Actions)
        {
            if (action is DislAction.NotAnObject) return Refuse($"The action at {action.At} is not an object.");
            if (action.When is { } when && !when.Holds(variables)) continue;
            if (!Action(action, variables)) return false;
        }
        return true;
    }

    public bool Refuse(string reason)
    {
        Refusal ??= reason;
        return false;
    }

    /// <summary>Runs one action by its kind; false once the transaction is refused.</summary>
    private bool Action(DislAction action, Dictionary<string, object?> variables) => action switch
    {
        DislAction.Set set => Set(set, variables),
        DislAction.Unset unset => Unset(unset, variables),
        DislAction.Create create => Create(create, variables),
        DislAction.Connect connect => Connect(connect, variables),
        DislAction.Retype retype => Retype(retype, variables),
        DislAction.Delete delete => Delete(delete, variables),
        DislAction.Reparent reparent => Reparent(reparent, variables),
        DislAction.Let let => Let(let, variables),
        DislAction.If condition => If(condition, variables),
        DislAction.ForEach forEach => ForEach(forEach, variables),
        DislAction.Select select => Select(select, variables),
        DislAction.EditLabel editLabel => EditLabel(editLabel, variables),
        DislAction.Layout layout => Layout(layout, variables),
        DislAction.View view => View(view, variables),
        DislAction.Plugin plugin => Plugin(plugin, variables),
        DislAction.Abort abort => Abort(abort, variables),
        DislAction.Unsupported unsupported => Refuse($"This runtime cannot run a `{unsupported.Kind}` action yet ({unsupported.At})."),
        _ => Refuse($"The action at {action.At} is none this runtime knows."),
    };

    /// <summary>A <c>set</c>: its values stored on its <c>target</c>, by default <c>self</c>.</summary>
    private bool Set(DislAction.Set set, Dictionary<string, object?> variables)
    {
        if (!Target(set.Target, set.At, variables, out var target)) return false;
        Malformed(set.Malformed);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, DislActionValue declared) in set.Values)
        {
            if (!Value(declared, variables, out var value)) return false;
            values[name] = Stored(value);
        }
        if (Typed && !InRange(target, values, variables)) return false;
        return Store(target, values);
    }

    /// <summary>An <c>unset</c>: its attributes of its <c>target</c>, by default <c>self</c>, stored as null.</summary>
    private bool Unset(DislAction.Unset unset, Dictionary<string, object?> variables) =>
        Target(unset.Target, unset.At, variables, out var target) && Store(target, unset.Names.ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal));

    /// <summary>A <c>delete</c>: each element its value gives taken out, with what <see cref="DeletionPolicy"/> takes out with it.</summary>
    private bool Delete(DislAction.Delete delete, Dictionary<string, object?> variables)
    {
        if (!Value(delete.Elements, variables, out var value)) return false;
        foreach (var element in Elements(value))
        {
            var deletion = DeletionPolicy.Changes(specification, element);
            if (!deletion.WasApplied) return Refuse(deletion.Refusal!);
            foreach (var change in deletion.Changes)
            {
                _changes.Add(change);
                if (change is DislChange.Remove removed && diagram.Elements.FirstOrDefault(candidate => candidate.Id == removed.ElementId) is { } gone) diagram.Remove(gone);
            }
        }
        return true;
    }

    /// <summary>A <c>reparent</c>: its <c>target</c>, by default <c>self</c>, moved under its <c>parent</c> and <c>slot</c>, <c>after</c> or <c>before</c> a sibling, or last.</summary>
    private bool Reparent(DislAction.Reparent reparent, Dictionary<string, object?> variables)
    {
        var at = reparent.At;
        if (!Member(reparent.Target, variables, variables.GetValueOrDefault("self"), out var target)
            || !Member(reparent.Parent, variables, null, out var parent)
            || !Member(reparent.Slot, variables, null, out var slot)
            || !Member(reparent.After, variables, null, out var after)
            || !Member(reparent.Before, variables, null, out var before)) return false;
        if (target is not DislElement element) return Refuse($"The reparent at {at} has no element to move.");
        var newParent = parent as DislElement;
        var siblings = diagram.SiblingsUnder(newParent);
        var index = -1;
        if (after is DislElement previous && (index = siblings.IndexOf(previous)) >= 0) index++;
        else if (before is DislElement next) index = siblings.IndexOf(next);
        if (index < 0 && (after is DislElement || before is DislElement)) return Refuse($"The reparent at {at} places the element beside one that is not beneath its new parent.");
        try
        {
            diagram.Reparent(element, newParent, slot as string, index);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return Refuse(e.Message);
        }
        _changes.Add(new DislChange.Reparent(element.Id, newParent?.Id, index));
        return true;
    }

    /// <summary>A <c>let</c>: its names bound, in order, for the actions after it.</summary>
    private bool Let(DislAction.Let let, Dictionary<string, object?> variables)
    {
        Malformed(let.Malformed);
        foreach ((string name, DislActionValue declared) in let.Bindings)
        {
            if (!Value(declared, variables, out var value)) return false;
            variables[name] = value;
        }
        return true;
    }

    /// <summary>An <c>if</c>: its <c>then</c> while its condition holds, else its <c>else</c>.</summary>
    private bool If(DislAction.If condition, Dictionary<string, object?> variables) =>
        condition.Condition.Holds(variables)
            ? Run(condition.Then, variables)
            : Run(condition.Else, variables);

    /// <summary>A <c>forEach</c>: its <c>do</c> for each item of its list, the item bound to its <c>as</c>.</summary>
    private bool ForEach(DislAction.ForEach forEach, Dictionary<string, object?> variables)
    {
        if (!Value(forEach.Items, variables, out var list)) return false;
        if (list is not IReadOnlyList<object?> items) return Refuse($"The forEach at {forEach.At} is not over a list.");
        foreach (var item in items)
        {
            variables[forEach.As] = item;
            if (!Run(forEach.Do, variables)) return false;
        }
        return true;
    }

    /// <summary>A <c>select</c>: the elements its value gives, handed to the host.</summary>
    private bool Select(DislAction.Select select, Dictionary<string, object?> variables)
    {
        if (!Value(select.Elements, variables, out var value)) return false;
        _host.Add(new HostAction.Select([.. Elements(value).Select(element => element.Id)]));
        return true;
    }

    /// <summary>An <c>editLabel</c>: its <c>target</c>, by default <c>self</c>, and its <c>label</c>, handed to the host.</summary>
    private bool EditLabel(DislAction.EditLabel editLabel, Dictionary<string, object?> variables)
    {
        if (!Member(editLabel.Target, variables, variables.GetValueOrDefault("self"), out var target)) return false;
        if (target is not DislElement element) return Refuse($"The editLabel at {editLabel.At} has no element to edit.");
        _host.Add(new HostAction.EditLabel(element.Id, editLabel.Label));
        return true;
    }

    /// <summary>A <c>layout</c>: its <c>algorithm</c> and its <c>refusals</c>, evaluated, handed to the host.</summary>
    private bool Layout(DislAction.Layout layout, Dictionary<string, object?> variables)
    {
        _host.Add(new HostAction.Layout(layout.Algorithm, layout.NothingDrawn?.Text(variables, layout.Algorithm), layout.Unchanged?.Text(variables, layout.Algorithm)));
        return true;
    }

    /// <summary>A <c>view</c>: its values for its <c>target</c>, by default <c>self</c>, handed to the host.</summary>
    private bool View(DislAction.View view, Dictionary<string, object?> variables)
    {
        if (!Target(view.Target, view.At, variables, out var target)) return false;
        Malformed(view.Malformed);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in view.Values)
        {
            if (property.Map is { } entries)
            {
                // A map of expressions by filter id or group key, not one expression.
                if (!Values(entries, variables, out var map)) return false;
                values[property.Name] = map;
                continue;
            }

            if (!Value(property.Value!, variables, out var value)) return false;
            values[property.Name] = value;
        }

        _host.Add(new HostAction.View(target.Id, values));
        return true;
    }

    /// <summary>A <c>plugin</c>: the plugin it names and its <c>args</c>, handed to the host.</summary>
    private bool Plugin(DislAction.Plugin plugin, Dictionary<string, object?> variables)
    {
        if (!Values(plugin.Arguments, variables, out var arguments)) return false;
        Malformed(plugin.Malformed);
        _host.Add(new HostAction.Plugin(plugin.Name, arguments));
        return true;
    }

    /// <summary>An <c>abort</c>: the transaction refused with its <c>message</c>, or the runtime's own.</summary>
    private bool Abort(DislAction.Abort abort, Dictionary<string, object?> variables) =>
        Member(abort.Message, variables, "The change was refused.", out var message) && Refuse(DislEvaluation.TextOf(message));

    /// <summary>
    /// Throws what reading a body of the wrong shape threw when the runtime read it from the JSON, at the moment it did:
    /// a definition's mistake the runtime never refused, and does not refuse now.
    /// </summary>
    private static void Malformed(string? message)
    {
        if (message is not null) throw new InvalidOperationException(message);
    }

    private bool Create(DislAction.Create create, Dictionary<string, object?> variables)
    {
        var at = create.BodyAt;
        if (!Member(create.Type, variables, null, out var typeValue)) return false;
        if (typeValue is not string type || specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false }) return Refuse($"The create at {at} names no node type it can create.");
        if (!Member(create.Parent, variables, null, out var parent)
            || !Member(create.Slot, variables, null, out var slot)
            || !Member(create.Point, variables, null, out var point)) return false;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, DislActionValue declared) in create.Attributes)
        {
            if (!Value(declared, variables, out var value)) return false;
            if (value is not null) attributes[name] = Stored(value);
        }

        DislElement created;
        try
        {
            created = diagram.AddNode(type, ids.Next(type), attributes, parent as DislElement, slot as string);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return Refuse(e.Message);
        }
        _changes.Add(new DislChange.Create(type, created.Id, attributes, (parent as DislElement)?.Id, point));
        if (create.As is { } bound) variables[bound] = created;
        return true;
    }

    /// <summary>
    /// A <c>connect</c> (§9.4): a relation of its <c>type</c> from its <c>source</c> to its <c>target</c>, both
    /// elements of the working diagram, with its <c>attributes</c>, under a new id; <c>as</c> names it.
    /// </summary>
    private bool Connect(DislAction.Connect connect, Dictionary<string, object?> variables)
    {
        var at = connect.BodyAt;
        if (!Member(connect.Type, variables, null, out var typeValue)) return false;
        if (typeValue is not string type || specification.Metamodel.TypeOf(type) is not { IsRelation: true, Abstract: false }) return Refuse($"The connect at {at} names no relation type it can create.");
        if (!Member(connect.Source, variables, null, out var source) || !Member(connect.Target, variables, null, out var target)) return false;
        if (source is not DislElement from || target is not DislElement to) return Refuse($"The connect at {at} does not name two elements to connect.");

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, DislActionValue declared) in connect.Attributes)
        {
            if (!Value(declared, variables, out var value)) return false;
            if (value is not null) attributes[name] = Stored(value);
        }

        DislElement created;
        try
        {
            created = diagram.AddRelation(type, ids.Next(type), from, to, attributes);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return Refuse(e.Message);
        }
        _changes.Add(new DislChange.Connect(type, created.Id, from.Id, to.Id, attributes));
        if (connect.As is { } bound) variables[bound] = created;
        return true;
    }

    /// <summary>
    /// A <c>retype</c> (§9.4): its <c>target</c>, by default <c>self</c>, made the type <c>to</c> names, as
    /// <c>behavior.retype</c> allows (<see cref="RetypePolicy"/>). The element changes type in the working
    /// diagram, so the actions after it see it as the new type.
    /// </summary>
    private bool Retype(DislAction.Retype retype, Dictionary<string, object?> variables)
    {
        var at = retype.BodyAt;
        if (!Member(retype.Target, variables, variables.GetValueOrDefault("self"), out var target)
            || !Member(retype.To, variables, null, out var to)) return false;
        if (target is not DislElement element) return Refuse($"The retype at {at} has no element to change.");
        if (to is not string type) return Refuse($"The retype at {at} names no type.");

        var transaction = RetypePolicy.Change(specification, element, type);
        if (!transaction.WasApplied) return Refuse(transaction.Refusal!);
        var change = (DislChange.Retype)transaction.Changes[0];
        if (Before is not null && !Before.ContainsKey(element)) Before[element] = element.Snapshot();
        element.Retype(specification.Metamodel.TypeOf(type)!, change.Attributes);
        _changes.Add(change);
        return true;
    }

    /// <summary>
    /// Holds each typed number of <paramref name="values"/> to its attribute's bounds (§4.3): stopped at the bound under
    /// <c>typed: "clamp"</c>, else the transaction is refused with <c>outOfRange.message</c> in the context
    /// <c>gesture:change</c> plus <c>min</c> and <c>max</c>, or with the runtime's sentence.
    /// </summary>
    internal bool InRange(DislElement target, Dictionary<string, object?> values, Dictionary<string, object?> variables)
    {
        foreach ((string name, object? value) in values.ToList())
        {
            if (target.Type.Attributes.GetValueOrDefault(name) is not { } attribute) continue;
            if (value is not (double or long)) continue;
            var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            var bound = attribute.Min is { } min && number < min ? min : attribute.Max is { } max && number > max ? max : (double?)null;
            if (bound is not { } limit) continue;
            if (attribute.TypedOutOfRange == "clamp")
            {
                values[name] = value is long && limit == Math.Floor(limit) ? (long)limit : limit;
                continue;
            }
            var context = new Dictionary<string, object?>(variables)
            {
                ["self"] = target, ["attribute"] = name, ["oldValue"] = target.ValueOf(name), ["newValue"] = value,
                ["min"] = attribute.Min, ["max"] = attribute.Max,
            };
            var noun = limit == attribute.Min ? "minimum" : "maximum";
            var sentence = $"'{target.Id}' cannot have {name} {Invariant(number)}; the {noun} is {Invariant(limit)}.";
            return Refuse(attribute.OutOfRangeMessage?.Text(context, sentence) ?? sentence);
        }
        return true;

        static string Invariant(double number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Stores <paramref name="values"/> on <paramref name="target"/>, as one change.</summary>
    private bool Store(DislElement target, Dictionary<string, object?> values)
    {
        if (Before is not null && !Before.ContainsKey(target)) Before[target] = target.Snapshot();
        try
        {
            foreach ((string name, object? value) in values) target.Store(name, value);
        }
        catch (ArgumentException e)
        {
            return Refuse(e.Message);
        }
        if (values.Count > 0) _changes.Add(new DislChange.Set(target.Id, target.Type.Name, values));
        return true;
    }

    /// <summary>The action's <c>target</c>, or <c>self</c> when it names none; refused when it is no element.</summary>
    private bool Target(DislActionValue? declared, string at, Dictionary<string, object?> variables, out DislElement target)
    {
        target = null!;
        object? value = variables.GetValueOrDefault("self");
        if (declared is not null && !Value(declared, variables, out value)) return false;
        if (value is DislElement element)
        {
            target = element;
            return true;
        }
        return Refuse($"The action at {at} has no element to change.");
    }

    /// <summary>The values of named expressions, by name, in order; false at the first that fails.</summary>
    private bool Values(IReadOnlyList<(string Name, DislActionValue Value)> declared, Dictionary<string, object?> variables, out Dictionary<string, object?> values)
    {
        values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, DislActionValue expression) in declared)
        {
            if (!Value(expression, variables, out var value)) return false;
            values[name] = value;
        }
        return true;
    }

    /// <summary>A member of an action's body: its value, or <paramref name="fallback"/> when the body states none.</summary>
    private bool Member(DislActionValue? declared, Dictionary<string, object?> variables, object? fallback, out object? value)
    {
        value = fallback;
        return declared is null || Value(declared, variables, out value);
    }

    private bool Value(DislActionValue declared, Dictionary<string, object?> variables, out object? value)
    {
        value = declared.Evaluate(variables);
        return value is not CelError error || Refuse($"{declared.Pointer}: {error.Message}");
    }

    /// <summary>A value as an element stores it: an element by its id, a list item by item.</summary>
    private static object? Stored(object? value) => value switch
    {
        DislElement element => element.Id,
        IReadOnlyList<object?> list => list.Select(Stored).ToList(),
        _ => value,
    };

    private static IEnumerable<DislElement> Elements(object? value) => value switch
    {
        DislElement element => [element],
        IReadOnlyList<object?> list => list.OfType<DislElement>(),
        _ => [],
    };
}
