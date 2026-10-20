using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads <c>constraints</c> (DISL §8) into <see cref="DislConstraints"/>: the rules, their defaults and order, and the
/// built-ins' settings, every expression with the program the loader compiled at its pointer.
/// </summary>
/// <remarks>
/// Reading never refuses: what the runtime threw on, skipped or read as absent when it read the JSON at run time is
/// read as a record that does the same when it is used.
/// </remarks>
internal static class DislConstraintReader
{
    private const string Pointer = "/constraints/rules";

    public static DislConstraints Read(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("constraints", out var constraints) || constraints.ValueKind != JsonValueKind.Object) return DislConstraints.None;

        List<DislConstraintRule>? rules = null;
        if (constraints.TryGetProperty("rules", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            rules = [];
            var index = 0;
            foreach (var rule in declared.EnumerateArray()) rules.Add(Rule(specification, rule, DislJson.Pointer(Pointer, index++)));
        }

        var hasDefaults = constraints.TryGetProperty("defaults", out var defaults);
        var builtIns = new Dictionary<string, DislBuiltIn>(StringComparer.Ordinal);
        foreach (var builtIn in DislJson.Members(constraints, "builtIn"))
        {
            if (builtIn.Value.ValueKind == JsonValueKind.Object) builtIns[builtIn.Name] = BuiltIn(specification, builtIn.Value, DislJson.Pointer("/constraints/builtIn", builtIn.Name));
        }
        return new DislConstraints(
            rules,
            hasDefaults ? DislJson.Strings(defaults, "timing") : [],
            hasDefaults && defaults.ValueKind == JsonValueKind.Object ? DislJson.String(defaults, "severity") : null,
            constraints.TryGetProperty("order", out var order) ? DislJson.Strings(order) : [],
            builtIns);
    }

    private static DislConstraintRule Rule(DislSpecification specification, JsonElement rule, string pointer)
    {
        var id = DislJson.String(rule, "id") ?? pointer;
        if (DislFieldReader.NotAnObject(rule) is { } malformed) return new DislConstraintRule { Pointer = pointer, Id = id, Code = id, NotAnObject = malformed };

        var severity = rule.TryGetProperty("severity", out var declaredSeverity) ? declaredSeverity : default;
        return new DislConstraintRule
        {
            Pointer = pointer,
            Id = id,
            Code = DislJson.String(rule, "code") ?? id,
            Kind = DislJson.String(rule, "kind"),
            Enabled = !rule.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            Enforcement = DislJson.String(rule, "enforcement"),
            Timing = DislJson.Strings(rule, "timing"),
            Scope = DislJson.Strings(rule, "scope"),
            ForEach = Expression("forEach"),
            When = Expression("when"),
            Rule = Expression("rule"),
            SeverityText = severity.ValueKind == JsonValueKind.String ? severity.GetString() : null,
            Severity = severity.ValueKind is JsonValueKind.Undefined or JsonValueKind.String ? null : DislFieldReader.Expression(specification, severity, DislJson.Pointer(pointer, "severity")),
            Message = rule.TryGetProperty("message", out var message) ? DislFieldReader.Message(specification, message, DislJson.Pointer(pointer, "message")) : null,
            Location = Expression("location"),
            Subject = Expression("subject"),
            Target = rule.TryGetProperty("target", out var target) && !(target.ValueKind == JsonValueKind.String && target.GetString() == "self") ? Expression("target") : null,
        };

        DislExpressionField? Expression(string name) =>
            rule.TryGetProperty(name, out var json) ? DislFieldReader.Expression(specification, json, DislJson.Pointer(pointer, name)) : null;
    }

    private static DislBuiltIn BuiltIn(DislSpecification specification, JsonElement setting, string pointer)
    {
        var code = setting.TryGetProperty("code", out var declared) ? declared : default;
        return new DislBuiltIn(
            !setting.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            DislJson.String(setting, "severity"),
            DislJson.String(setting, "oncePerGroup"),
            code.ValueKind == JsonValueKind.String ? code.GetString() : null,
            code.ValueKind == JsonValueKind.Object ? DislFieldReader.Expression(specification, code, DislJson.Pointer(pointer, "code")) : null,
            setting.TryGetProperty("message", out var message) ? DislFieldReader.Message(specification, message, DislJson.Pointer(pointer, "message")) : null);
    }
}
