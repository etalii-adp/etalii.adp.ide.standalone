using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// An id rule's <c>ephemeral</c> and <c>reason</c> (DISL §11.5.3), typed at load: whether the ids it gives identify an
/// element within one reading of the model only, and the refusal shown when a gesture would store one.
/// </summary>
/// <param name="Always">The rule states <c>ephemeral: true</c>.</param>
/// <param name="When">The rule's <c>ephemeral</c> Expression, in the identity context; null when it states a bool or nothing.</param>
/// <param name="Reason">The rule's <c>reason</c>, in the element context; null when it states none.</param>
internal sealed record DislEphemerality(bool Always, DislExpressionField? When, DislReason? Reason)
{
    /// <summary>The <c>ephemeral</c> and <c>reason</c> of the rule <paramref name="rule"/> at <paramref name="pointer"/>.</summary>
    public static DislEphemerality Read(DislSpecification specification, JsonElement rule, string pointer)
    {
        if (rule.ValueKind != JsonValueKind.Object) return new DislEphemerality(false, null, null);
        var always = rule.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.ValueKind == JsonValueKind.True;
        var when = ephemeral.ValueKind is JsonValueKind.String or JsonValueKind.Object
            ? DislFieldReader.Expression(specification, ephemeral, JsonPointer.Append(pointer, "ephemeral"))
            : null;
        var reason = rule.TryGetProperty("reason", out var declared)
            ? DislFieldReader.Reason(specification, declared, JsonPointer.Append(pointer, "reason"))
            : null;
        return new DislEphemerality(always, when, reason);
    }

    /// <summary>Whether <paramref name="element"/> is ephemeral under this rule; an expression that fails does not make it so.</summary>
    public bool Holds(DislElement element) => Always || When is not null && When.Holds(Variables(element));

    /// <summary>The variables of the identity and element contexts, which both bind <c>self</c>, <c>diagram</c> and <c>env</c>.</summary>
    public static Dictionary<string, object?> Variables(DislElement element) =>
        new(StringComparer.Ordinal) { ["self"] = element, ["diagram"] = element.Diagram, ["env"] = element.Diagram.Env.ToCel() };
}
