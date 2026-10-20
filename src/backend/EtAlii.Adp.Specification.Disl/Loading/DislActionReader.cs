using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads an action list (DISL §9.4) into <see cref="DislAction"/>s: each action's kind, the first of
/// <see cref="Kinds"/> it carries, its members, and every value position with the program compiled at its pointer.
/// </summary>
/// <remarks>
/// Reading never refuses and never reports: what <see cref="ActionRunner"/> refuses or throws on at run time - an item
/// that is not an object, an unknown kind, a kind it cannot run yet, a body of the wrong shape - is read as a case
/// that does exactly that when it runs, so typing the list changes no outcome and moves none to load.
/// </remarks>
internal static class DislActionReader
{
    /// <summary>Every kind of action §9.4 names, in the order the runtime looks for them.</summary>
    private static readonly string[] Kinds =
    [
        "set", "unset", "create", "connect", "delete", "move", "resize", "retype", "reparent", "reorder", "view",
        "let", "if", "forEach", "select", "reveal", "highlight", "editLabel", "openForm", "notify", "layout", "abort", "call", "plugin",
    ];

    /// <summary>The action list at <paramref name="pointer"/>; an empty one for a value that is not a list.</summary>
    public static DislActionList List(DislSpecification specification, JsonElement actions, string pointer)
    {
        if (actions.ValueKind != JsonValueKind.Array) return DislActionList.Empty;
        var list = new List<DislAction>();
        var index = 0;
        foreach (var action in actions.EnumerateArray()) list.Add(Action(specification, action, DislJson.Pointer(pointer, index++)));
        return new DislActionList(list);
    }

    /// <summary>The list in member <paramref name="name"/> of <paramref name="owner"/>; an empty one when it has none.</summary>
    private static DislActionList Nested(DislSpecification specification, JsonElement owner, string at, string name) =>
        owner.TryGetProperty(name, out var actions) ? List(specification, actions, DislJson.Pointer(at, name)) : DislActionList.Empty;

    private static DislAction Action(DislSpecification specification, JsonElement action, string at)
    {
        if (action.ValueKind != JsonValueKind.Object) return new DislAction.NotAnObject(at);
        var when = action.TryGetProperty("when", out var condition) ? DislFieldReader.Expression(specification, condition, DislJson.Pointer(at, "when")) : null;

        var kind = Kinds.FirstOrDefault(name => action.TryGetProperty(name, out _));
        if (kind is null) return new DislAction.Unknown(at, when);
        var body = action.GetProperty(kind);
        var bodyAt = DislJson.Pointer(at, kind);
        return kind switch
        {
            "set" => new DislAction.Set(at, when, Target(specification, action, at), Members(specification, body, bodyAt), DislFieldReader.NotAnObject(body)),
            "unset" => new DislAction.Unset(at, when, Target(specification, action, at), DislJson.Strings(body)),
            "create" => new DislAction.Create(
                at,
                when,
                bodyAt,
                Member(specification, body, bodyAt, "type"),
                Member(specification, body, bodyAt, "parent"),
                Member(specification, body, bodyAt, "slot"),
                Member(specification, body, bodyAt, "at"),
                Attributes(specification, body, bodyAt),
                DislJson.String(action, "as")),
            "connect" => new DislAction.Connect(
                at,
                when,
                bodyAt,
                Member(specification, body, bodyAt, "type"),
                Member(specification, body, bodyAt, "source"),
                Member(specification, body, bodyAt, "target"),
                Attributes(specification, body, bodyAt),
                DislJson.String(action, "as")),
            "retype" => new DislAction.Retype(at, when, bodyAt, Member(specification, body, bodyAt, "target"), Member(specification, body, bodyAt, "to")),
            "delete" => new DislAction.Delete(at, when, DislFieldReader.Value(specification, body, bodyAt)),
            "reparent" => new DislAction.Reparent(
                at,
                when,
                Member(specification, body, bodyAt, "target"),
                Member(specification, body, bodyAt, "parent"),
                Member(specification, body, bodyAt, "slot"),
                Member(specification, body, bodyAt, "after"),
                Member(specification, body, bodyAt, "before")),
            "let" => new DislAction.Let(at, when, Members(specification, body, bodyAt), DislFieldReader.NotAnObject(body)),
            "if" => new DislAction.If(at, when, DislFieldReader.Expression(specification, body, bodyAt), Nested(specification, action, at, "then"), Nested(specification, action, at, "else")),
            "forEach" => new DislAction.ForEach(at, when, DislFieldReader.Value(specification, body, bodyAt), DislJson.String(action, "as") ?? "item", Nested(specification, action, at, "do")),
            "select" => new DislAction.Select(at, when, DislFieldReader.Value(specification, body, bodyAt)),
            "editLabel" => new DislAction.EditLabel(at, when, Member(specification, body, bodyAt, "target"), body.ValueKind == JsonValueKind.Object ? DislJson.String(body, "label") : null),
            "layout" => new DislAction.Layout(at, when, DislJson.String(body, "algorithm") ?? ""),
            "view" => new DislAction.View(at, when, Target(specification, action, at), View(specification, body, bodyAt), DislFieldReader.NotAnObject(body)),
            "plugin" => Plugin(specification, action, at, when, body),
            "abort" => new DislAction.Abort(at, when, Member(specification, body, bodyAt, "message")),
            _ => new DislAction.Unsupported(at, when, kind),
        };
    }

    /// <summary>The action's <c>target</c>, or null when it names none and the runner takes <c>self</c>.</summary>
    private static DislActionValue? Target(DislSpecification specification, JsonElement action, string at) =>
        action.TryGetProperty("target", out var target) ? DislFieldReader.Value(specification, target, DislJson.Pointer(at, "target")) : null;

    /// <summary>The member <paramref name="name"/> of a body, or null when the body is not an object or has no such member.</summary>
    private static DislActionValue? Member(DislSpecification specification, JsonElement body, string at, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) ? DislFieldReader.Value(specification, value, DislJson.Pointer(at, name)) : null;

    /// <summary>Every member of an object body, in order; none for a body that is not an object, which the action's <c>Malformed</c> then speaks for.</summary>
    private static List<(string Name, DislActionValue Value)> Members(DislSpecification specification, JsonElement body, string at) =>
        body.ValueKind == JsonValueKind.Object
            ? [.. body.EnumerateObject().Select(property => (property.Name, DislFieldReader.Value(specification, property.Value, DislJson.Pointer(at, property.Name))))]
            : [];

    /// <summary>The <c>attributes</c> of a <c>create</c> or <c>connect</c> body, in order; none when it states none or they are not an object.</summary>
    private static List<(string Name, DislActionValue Value)> Attributes(DislSpecification specification, JsonElement body, string at)
    {
        var attributesAt = DislJson.Pointer(at, "attributes");
        return [.. DislJson.Members(body, "attributes").Select(property => (property.Name, DislFieldReader.Value(specification, property.Value, DislJson.Pointer(attributesAt, property.Name))))];
    }

    /// <summary>A <c>view</c> body's properties: <c>filters</c> and <c>groups</c> as maps when they are objects, every other one a value.</summary>
    private static List<DislViewValue> View(DislSpecification specification, JsonElement body, string at)
    {
        if (body.ValueKind != JsonValueKind.Object) return [];
        var values = new List<DislViewValue>();
        foreach (var property in body.EnumerateObject())
        {
            var propertyAt = DislJson.Pointer(at, property.Name);
            values.Add(property.Name is "filters" or "groups" && property.Value.ValueKind == JsonValueKind.Object
                ? new DislViewValue(property.Name, null, Members(specification, property.Value, propertyAt))
                : new DislViewValue(property.Name, DislFieldReader.Value(specification, property.Value, propertyAt), null));
        }
        return values;
    }

    /// <summary>A <c>plugin</c> action: its <c>args</c>, and the plugin its body names; a body that is neither a string nor null threw once the args were read.</summary>
    private static DislAction.Plugin Plugin(DislSpecification specification, JsonElement action, string at, DislExpressionField? when, JsonElement body)
    {
        var argumentsAt = DislJson.Pointer(at, "args");
        var arguments = DislJson.Members(action, "args").Select(argument => (argument.Name, DislFieldReader.Value(specification, argument.Value, DislJson.Pointer(argumentsAt, argument.Name)))).ToList();
        try
        {
            return new DislAction.Plugin(at, when, arguments, body.GetString() ?? "", null);
        }
        catch (InvalidOperationException e)
        {
            return new DislAction.Plugin(at, when, arguments, "", e.Message);
        }
    }
}
