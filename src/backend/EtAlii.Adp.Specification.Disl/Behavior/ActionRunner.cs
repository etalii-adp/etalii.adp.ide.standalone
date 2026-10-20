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

    private bool Action(DislAction action, Dictionary<string, object?> variables)
    {
        var at = action.At;
        switch (action)
        {
            case DislAction.Set set:
            {
                if (!Target(set.Target, at, variables, out var target)) return false;
                Malformed(set.Malformed);
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach ((string name, DislActionValue declared) in set.Values)
                {
                    if (!Value(declared, variables, out var value)) return false;
                    values[name] = Stored(value);
                }
                return Set(target, values);
            }
            case DislAction.Unset unset:
                {
                    return Target(unset.Target, at, variables, out var target) && Set(target, unset.Names.ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal));
                }
            case DislAction.Create create:
                return Create(create, variables);
            case DislAction.Connect connect:
                return Connect(connect, variables);
            case DislAction.Retype retype:
                return Retype(retype, variables);
            case DislAction.Delete delete:
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
            case DislAction.Reparent reparent:
            {
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
            case DislAction.Let let:
                Malformed(let.Malformed);
                foreach ((string name, DislActionValue declared) in let.Bindings)
                {
                    if (!Value(declared, variables, out var value)) return false;
                    variables[name] = value;
                }
                return true;
            case DislAction.If condition:
                return condition.Condition.Holds(variables)
                    ? Run(condition.Then, variables)
                    : Run(condition.Else, variables);
            case DislAction.ForEach forEach:
            {
                if (!Value(forEach.Items, variables, out var list)) return false;
                if (list is not IReadOnlyList<object?> items) return Refuse($"The forEach at {at} is not over a list.");
                foreach (var item in items)
                {
                    variables[forEach.As] = item;
                    if (!Run(forEach.Do, variables)) return false;
                }
                return true;
            }
            case DislAction.Select select:
            {
                if (!Value(select.Elements, variables, out var value)) return false;
                _host.Add(new HostAction.Select([.. Elements(value).Select(element => element.Id)]));
                return true;
            }
            case DislAction.EditLabel editLabel:
            {
                if (!Member(editLabel.Target, variables, variables.GetValueOrDefault("self"), out var target)) return false;
                if (target is not DislElement element) return Refuse($"The editLabel at {at} has no element to edit.");
                _host.Add(new HostAction.EditLabel(element.Id, editLabel.Label));
                return true;
            }
            case DislAction.Layout layout:
            {
                _host.Add(new HostAction.Layout(layout.Algorithm));
                return true;
            }
            case DislAction.View view:
            {
                if (!Target(view.Target, at, variables, out var target)) return false;
                Malformed(view.Malformed);
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in view.Values)
                {
                    if (property.Map is { } entries)
                    {
                        // A map of expressions by filter id or group key, not one expression.
                        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                        foreach ((string key, DislActionValue declared) in entries)
                        {
                            if (!Value(declared, variables, out var entryValue)) return false;
                            map[key] = entryValue;
                        }
                        values[property.Name] = map;
                        continue;
                    }

                    if (!Value(property.Value!, variables, out var value)) return false;
                    values[property.Name] = value;
                }

                _host.Add(new HostAction.View(target.Id, values));
                return true;
            }
            case DislAction.Plugin plugin:
            {
                var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach ((string name, DislActionValue declared) in plugin.Arguments)
                {
                    if (!Value(declared, variables, out var value)) return false;
                    arguments[name] = value;
                }
                Malformed(plugin.Malformed);
                _host.Add(new HostAction.Plugin(plugin.Name, arguments));
                return true;
            }
            case DislAction.Abort abort:
                return Member(abort.Message, variables, "The change was refused.", out var message) && Refuse(DislEvaluation.TextOf(message));
            case DislAction.Unsupported unsupported:
                return Refuse($"This runtime cannot run a `{unsupported.Kind}` action yet ({at}).");
            default:
                return Refuse($"The action at {at} is none this runtime knows.");
        }
    }

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

    private bool Set(DislElement target, Dictionary<string, object?> values)
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
