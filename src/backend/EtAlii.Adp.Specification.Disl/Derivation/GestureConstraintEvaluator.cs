using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The refusals of a gesture (DISL §8.4): every declared gesture constraint of its kind whose scope
/// takes the element, whose enforcement prevents, whose <c>when</c> holds and whose <c>rule</c> does
/// not, in the order of <c>rules</c>, each with its message evaluated in the gesture's context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared constraints only.</b> The built-ins that also refuse (§8.4, step 3) are checked by the
/// host where it has them; neither bundled definition configures one for a change or a placement.
/// </para>
/// <para>
/// <b>A rule that fails to evaluate refuses</b>: a gesture is let through only when its rule is seen
/// to hold. A <c>when</c> that fails does not apply the rule.
/// </para>
/// </remarks>
public static class GestureConstraintEvaluator
{
    /// <summary>A change of <paramref name="attribute"/> of <paramref name="self"/> from its value to <paramref name="newValue"/>.</summary>
    public static IReadOnlyList<DislFinding> Change(DislSpecification specification, DislElement self, string attribute, object? newValue, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        var oldValue = self.Type.Attributes.ContainsKey(attribute) ? self.ValueOf(attribute) : null;
        return Refusals(specification, "change", self, new Dictionary<string, object?>
        {
            ["attribute"] = attribute,
            ["oldValue"] = oldValue,
            ["newValue"] = newValue,
        }, env);
    }

    /// <summary>
    /// A placement of <paramref name="self"/>: a <paramref name="gesture"/> (<c>move</c>, <c>resize</c>,
    /// <c>reparent</c>) from <paramref name="oldBounds"/> to <paramref name="newBounds"/>, in domain values.
    /// </summary>
    public static IReadOnlyList<DislFinding> Placement(
        DislSpecification specification,
        DislElement self,
        string gesture,
        IReadOnlyDictionary<string, object?> oldBounds,
        IReadOnlyDictionary<string, object?> newBounds,
        DislElement? newParent = null,
        DislEnv? env = null) =>
        Refusals(specification, "placement", self, new Dictionary<string, object?>
        {
            ["gesture"] = gesture,
            ["oldBounds"] = Map(oldBounds),
            ["newBounds"] = Map(newBounds),
            ["newParent"] = newParent,
        }, env);

    /// <summary>The refusals of a gesture of <paramref name="kind"/> on <paramref name="self"/>, its own variables in <paramref name="gesture"/>.</summary>
    private static IReadOnlyList<DislFinding> Refusals(DislSpecification specification, string kind, DislElement? self, IReadOnlyDictionary<string, object?> gesture, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(gesture);

        List<DislFinding> refusals = [];
        if (!specification.Root.TryGetProperty("constraints", out var constraints) || constraints.ValueKind != JsonValueKind.Object
            || !constraints.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return refusals;
        }

        var context = DislContexts.OfConstraintKind(kind);
        var variables = new Dictionary<string, object?>(gesture)
        {
            ["self"] = self,
            ["diagram"] = self?.Diagram,
            ["env"] = (env ?? new DislEnv()).ToCel(),
        };
        var index = 0;
        foreach (var rule in rules.EnumerateArray())
        {
            var pointer = DislJson.Pointer("/constraints/rules", index++);
            if (DislJson.String(rule, "kind") != kind) continue;
            if (rule.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
            if ((DislJson.String(rule, "enforcement") ?? "prevent") is not ("prevent" or "prevent-and-report")) continue;
            var scope = DislJson.Strings(rule, "scope");
            if (scope.Count > 0 && !(self is not null && scope.Any(self.IsA)) && !scope.Contains("*")) continue;

            if (rule.TryGetProperty("when", out var when) && !DislEvaluation.Holds(specification, when, DislJson.Pointer(pointer, "when"), context, variables)) continue;
            if (rule.TryGetProperty("rule", out var condition) && DislEvaluation.Expression(specification, condition, DislJson.Pointer(pointer, "rule"), context, variables) is true) continue;

            var id = DislJson.String(rule, "id") ?? pointer;
            var message = rule.TryGetProperty("message", out var text)
                ? DislEvaluation.Message(specification, text, DislJson.Pointer(pointer, "message"), context, variables, id)
                : id;
            refusals.Add(new DislFinding(DislJson.String(rule, "code") ?? id, id, DislJson.String(rule, "severity") ?? "error", message, self is null ? [] : [self.Id], self?.Line));
        }
        return refusals;
    }

    private static CelMap Map(IReadOnlyDictionary<string, object?> values)
    {
        var map = new CelMap();
        foreach ((string key, object? value) in values) map[key] = value;
        return map;
    }
}
