using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads the positions every typed part of a specification shares - an Expression, an action's value, a Message, a
/// Reason - from its JSON, each expression with the program the loader compiled at its pointer.
/// </summary>
/// <remarks>
/// Each reading keeps what the runtime did with the JSON: a position that holds no expression is kept as one that
/// evaluates to a <see cref="Cel.CelError"/>, and a value of the wrong shape as the fallback it gave, so that typing
/// at load changes no outcome.
/// </remarks>
internal static class DislFieldReader
{
    /// <summary>An Expression property (§2.5 a) at <paramref name="pointer"/>: a bare string or the <c>cel</c> of an object.</summary>
    public static DislExpressionField Expression(DislSpecification specification, JsonElement json, string pointer)
    {
        (string? source, string at) = Source(json, pointer);
        return new DislExpressionField(pointer, source, source is null ? null : specification.CompiledAt(at, source));
    }

    /// <summary>The CEL of an Expression property and the pointer the loader compiled it at; no source for any other value.</summary>
    private static (string? Source, string At) Source(JsonElement json, string pointer) => json.ValueKind switch
    {
        JsonValueKind.String => (json.GetString(), pointer),
        JsonValueKind.Object when DislJson.String(json, "cel") is { } cel => (cel, DislJson.Pointer(pointer, "cel")),
        _ => (null, pointer),
    };

    /// <summary>A value position of an action (§2.5 d): a string or an object is an Expression, anything else the value it reads as.</summary>
    public static DislActionValue Value(DislSpecification specification, JsonElement json, string pointer) =>
        json.ValueKind is JsonValueKind.String or JsonValueKind.Object
            ? new DislActionValue.Expression(Expression(specification, json, pointer))
            : new DislActionValue.Literal(pointer, DislValues.Json(json));

    /// <summary>A Message (§2.3) at <paramref name="pointer"/>.</summary>
    public static DislMessage Message(DislSpecification specification, JsonElement json, string pointer) => json.ValueKind switch
    {
        JsonValueKind.String => new DislMessage.Plain(json.GetString()!),
        JsonValueKind.Object when json.TryGetProperty("cel", out _) => new DislMessage.Cel(Expression(specification, json, pointer)),
        JsonValueKind.Object => new DislMessage.Localized(DislEvaluation.Localized(json.EnumerateObject().Select(entry => (entry.Name, entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null)))),
        _ => new DislMessage.None(),
    };

    /// <summary>A list of Reasons (§2.3) at <paramref name="pointer"/>; none when it is not a list.</summary>
    public static IReadOnlyList<DislReason> Reasons(DislSpecification specification, JsonElement json, string pointer)
    {
        if (json.ValueKind != JsonValueKind.Array) return [];
        var reasons = new List<DislReason>();
        var index = 0;
        foreach (var reason in json.EnumerateArray()) reasons.Add(Reason(specification, reason, DislJson.Pointer(pointer, index++)));
        return reasons;
    }

    /// <summary>One Reason (§2.3) at <paramref name="pointer"/>.</summary>
    public static DislReason Reason(DislSpecification specification, JsonElement json, string pointer)
    {
        if (json.ValueKind == JsonValueKind.Object && DislJson.String(json, "reason") is { } id) return new DislReason.Named(id);
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var message))
        {
            var when = json.TryGetProperty("when", out var condition) ? Expression(specification, condition, DislJson.Pointer(pointer, "when")) : null;
            return new DislReason.While(when, Message(specification, message, DislJson.Pointer(pointer, "message")));
        }
        return new DislReason.Always(Message(specification, json, pointer));
    }

    /// <summary>
    /// The message reading a member of <paramref name="json"/> throws when it is not an object, as System.Text.Json words
    /// it; null for an object. A position the runtime read with an object's members, and that threw at run time when it
    /// was something else, keeps that message, so that it still throws, at the same moment.
    /// </summary>
    public static string? NotAnObject(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object) return null;
        try
        {
            json.TryGetProperty("", out _);
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// The message reading <paramref name="json"/> as an integer throws when it is not a number, as System.Text.Json words it;
    /// null for a number. A position the runtime read as an integer, and that threw at run time when it was something
    /// else, keeps that message, so that it still throws, at the same moment.
    /// </summary>
    public static string? NotANumber(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number) return null;
        try
        {
            json.TryGetInt64(out _);
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e.Message;
        }
    }
}
