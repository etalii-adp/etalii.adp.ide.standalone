using System.Text.Json;
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
/// <c>layout</c>, <c>plugin</c>, <c>select</c> and <c>editLabel</c> are handed back as
/// <see cref="HostAction"/>s. <c>abort</c> refuses the transaction with its message. Any other action,
/// and an expression that fails, refuses it too: a runtime that cannot do what a definition says does
/// not do part of it.
/// </para>
/// <para>
/// <b>A <c>reparent</c> is applied like the others</b>: the node moves, with everything beneath it,
/// to its new parent at the place its <c>after</c> or <c>before</c> names, or last, and the change
/// records that place as an index among the new parent's children counted before the move.
/// </para>
/// </remarks>
internal sealed class ActionRunner(DislSpecification specification, DislDiagram diagram, IIdSource ids, string context)
{
    private static readonly string[] Kinds =
    [
        "set", "unset", "create", "connect", "delete", "move", "resize", "retype", "reparent", "reorder", "view",
        "let", "if", "forEach", "select", "reveal", "highlight", "editLabel", "openForm", "notify", "layout", "abort", "call", "plugin",
    ];

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

    /// <summary>Runs <paramref name="actions"/>, found at <paramref name="pointer"/>; false once the transaction is refused.</summary>
    public bool Run(JsonElement actions, string pointer, Dictionary<string, object?> variables)
    {
        if (actions.ValueKind != JsonValueKind.Array) return true;
        var index = 0;
        foreach (var action in actions.EnumerateArray())
        {
            var at = DislJson.Pointer(pointer, index++);
            if (action.ValueKind != JsonValueKind.Object) return Refuse($"The action at {at} is not an object.");
            if (action.TryGetProperty("when", out var when) && !DislEvaluation.Holds(specification, when, DislJson.Pointer(at, "when"), context, variables)) continue;
            if (!Action(action, at, variables)) return false;
        }
        return true;
    }

    public bool Refuse(string reason)
    {
        Refusal ??= reason;
        return false;
    }

    private bool Action(JsonElement action, string at, Dictionary<string, object?> variables)
    {
        var kind = Kinds.FirstOrDefault(name => action.TryGetProperty(name, out _));
        if (kind is null) return Refuse($"The action at {at} is none this runtime knows.");
        var body = action.GetProperty(kind);
        var bodyAt = DislJson.Pointer(at, kind);
        switch (kind)
        {
            case "set":
            {
                if (!Target(action, at, variables, out var target)) return false;
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in body.EnumerateObject())
                {
                    if (!Value(property.Value, DislJson.Pointer(bodyAt, property.Name), variables, out var value)) return false;
                    values[property.Name] = Stored(value);
                }
                return Set(target, values);
            }
            case "unset":
            {
                if (!Target(action, at, variables, out var target)) return false;
                return Set(target, DislJson.Strings(body).ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal));
            }
            case "create":
                return Create(action, body, bodyAt, variables);
            case "connect":
                return Connect(action, body, bodyAt, variables);
            case "retype":
                return Retype(body, bodyAt, variables);
            case "delete":
            {
                if (!Value(body, bodyAt, variables, out var value)) return false;
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
            case "reparent":
            {
                if (!Member(body, bodyAt, "target", variables, variables.GetValueOrDefault("self"), out var target)
                    || !Member(body, bodyAt, "parent", variables, null, out var parent)
                    || !Member(body, bodyAt, "slot", variables, null, out var slot)
                    || !Member(body, bodyAt, "after", variables, null, out var after)
                    || !Member(body, bodyAt, "before", variables, null, out var before)) return false;
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
                _changes.Add(new DislChange.Reparent(element.Id, newParent?.Id, slot as string, (after as DislElement)?.Id, (before as DislElement)?.Id, index));
                return true;
            }
            case "let":
                foreach (var property in body.EnumerateObject())
                {
                    if (!Value(property.Value, DislJson.Pointer(bodyAt, property.Name), variables, out var value)) return false;
                    variables[property.Name] = value;
                }
                return true;
            case "if":
                return DislEvaluation.Holds(specification, body, bodyAt, context, variables)
                    ? !action.TryGetProperty("then", out var then) || Run(then, DislJson.Pointer(at, "then"), variables)
                    : !action.TryGetProperty("else", out var otherwise) || Run(otherwise, DislJson.Pointer(at, "else"), variables);
            case "forEach":
            {
                if (!Value(body, bodyAt, variables, out var list)) return false;
                if (list is not IReadOnlyList<object?> items) return Refuse($"The forEach at {at} is not over a list.");
                var name = DislJson.String(action, "as") ?? "item";
                foreach (var item in items)
                {
                    variables[name] = item;
                    if (action.TryGetProperty("do", out var steps) && !Run(steps, DislJson.Pointer(at, "do"), variables)) return false;
                }
                return true;
            }
            case "select":
            {
                if (!Value(body, bodyAt, variables, out var value)) return false;
                _host.Add(new HostAction.Select([.. Elements(value).Select(element => element.Id)]));
                return true;
            }
            case "editLabel":
            {
                if (!Member(body, bodyAt, "target", variables, variables.GetValueOrDefault("self"), out var target)) return false;
                if (target is not DislElement element) return Refuse($"The editLabel at {at} has no element to edit.");
                _host.Add(new HostAction.EditLabel(element.Id, body.ValueKind == JsonValueKind.Object ? DislJson.String(body, "label") : null));
                return true;
            }
            case "layout":
            {
                if (!Member(body, bodyAt, "scope", variables, null, out var scope)) return false;
                _host.Add(new HostAction.Layout(DislJson.String(body, "algorithm") ?? "", scope));
                return true;
            }
            case "plugin":
            {
                var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var argument in DislJson.Members(action, "args"))
                {
                    if (!Value(argument.Value, DislJson.Pointer(DislJson.Pointer(at, "args"), argument.Name), variables, out var value)) return false;
                    arguments[argument.Name] = value;
                }
                _host.Add(new HostAction.Plugin(body.GetString() ?? "", arguments));
                return true;
            }
            case "abort":
            {
                if (!Member(body, bodyAt, "message", variables, "The change was refused.", out var message)) return false;
                return Refuse(DislEvaluation.TextOf(message));
            }
            default:
                return Refuse($"This runtime cannot run a `{kind}` action yet ({at}).");
        }
    }

    private bool Create(JsonElement action, JsonElement body, string at, Dictionary<string, object?> variables)
    {
        if (!Member(body, at, "type", variables, null, out var typeValue)) return false;
        if (typeValue is not string type || specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false }) return Refuse($"The create at {at} names no node type it can create.");
        if (!Member(body, at, "parent", variables, null, out var parent)
            || !Member(body, at, "slot", variables, null, out var slot)
            || !Member(body, at, "at", variables, null, out var point)) return false;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in DislJson.Members(body, "attributes"))
        {
            if (!Value(property.Value, DislJson.Pointer(DislJson.Pointer(at, "attributes"), property.Name), variables, out var value)) return false;
            if (value is not null) attributes[property.Name] = Stored(value);
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
        if (DislJson.String(action, "as") is { } name) variables[name] = created;
        return true;
    }

    /// <summary>
    /// A <c>connect</c> (§9.4): a relation of its <c>type</c> from its <c>source</c> to its <c>target</c>, both
    /// elements of the working diagram, with its <c>attributes</c>, under a new id; <c>as</c> names it.
    /// </summary>
    private bool Connect(JsonElement action, JsonElement body, string at, Dictionary<string, object?> variables)
    {
        if (!Member(body, at, "type", variables, null, out var typeValue)) return false;
        if (typeValue is not string type || specification.Metamodel.TypeOf(type) is not { IsRelation: true, Abstract: false }) return Refuse($"The connect at {at} names no relation type it can create.");
        if (!Member(body, at, "source", variables, null, out var source) || !Member(body, at, "target", variables, null, out var target)) return false;
        if (source is not DislElement from || target is not DislElement to) return Refuse($"The connect at {at} does not name two elements to connect.");

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in DislJson.Members(body, "attributes"))
        {
            if (!Value(property.Value, DislJson.Pointer(DislJson.Pointer(at, "attributes"), property.Name), variables, out var value)) return false;
            if (value is not null) attributes[property.Name] = Stored(value);
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
        if (DislJson.String(action, "as") is { } name) variables[name] = created;
        return true;
    }

    /// <summary>
    /// A <c>retype</c> (§9.4): its <c>target</c>, by default <c>self</c>, made the type <c>to</c> names, as
    /// <c>behavior.retype</c> allows (<see cref="RetypePolicy"/>). The element changes type in the working
    /// diagram, so the actions after it see it as the new type.
    /// </summary>
    private bool Retype(JsonElement body, string at, Dictionary<string, object?> variables)
    {
        if (!Member(body, at, "target", variables, variables.GetValueOrDefault("self"), out var target)
            || !Member(body, at, "to", variables, null, out var to)) return false;
        if (target is not DislElement element) return Refuse($"The retype at {at} has no element to change.");
        if (to is not string type) return Refuse($"The retype at {at} names no type.");

        var transaction = RetypePolicy.Change(specification, element, type);
        if (!transaction.WasApplied) return Refuse(transaction.Refusal!);
        var retype = (DislChange.Retype)transaction.Changes[0];
        if (Before is not null && !Before.ContainsKey(element)) Before[element] = element.Snapshot();
        element.Retype(specification.Metamodel.TypeOf(type)!, retype.Attributes);
        _changes.Add(retype);
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

    private bool Target(JsonElement action, string at, Dictionary<string, object?> variables, out DislElement target)
    {
        target = null!;
        object? value = variables.GetValueOrDefault("self");
        if (action.TryGetProperty("target", out var declared) && !Value(declared, DislJson.Pointer(at, "target"), variables, out value)) return false;
        if (value is DislElement element)
        {
            target = element;
            return true;
        }
        return Refuse($"The action at {at} has no element to change.");
    }

    private bool Member(JsonElement body, string at, string name, Dictionary<string, object?> variables, object? fallback, out object? value)
    {
        value = fallback;
        return body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var json) || Value(json, DislJson.Pointer(at, name), variables, out value);
    }

    private bool Value(JsonElement json, string at, Dictionary<string, object?> variables, out object? value)
    {
        value = json.ValueKind is JsonValueKind.String or JsonValueKind.Object
            ? DislEvaluation.Expression(specification, json, at, context, variables)
            : DislValues.Json(json);
        return value is not CelError error || Refuse($"{at}: {error.Message}");
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
