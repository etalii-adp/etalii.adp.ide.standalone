using System.Text.Json;
using EtAlii.Adp.Specification.Json;

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
            foreach (var rule in declared.EnumerateArray()) rules.Add(Rule(specification, rule, JsonPointer.Append(Pointer, index++)));
        }

        var hasDefaults = constraints.TryGetProperty("defaults", out var defaults);
        var builtIns = new Dictionary<string, DislBuiltIn>(StringComparer.Ordinal);
        foreach (var builtIn in DislJson.Members(constraints, "builtIn"))
        {
            if (builtIn.Value.ValueKind == JsonValueKind.Object) builtIns[builtIn.Name] = BuiltIn(specification, builtIn.Value, JsonPointer.Append("/constraints/builtIn", builtIn.Name));
        }
        return new DislConstraints(
            rules,
            hasDefaults ? JsonProperties.StringOrStrings(defaults, "timing") : [],
            hasDefaults && defaults.ValueKind == JsonValueKind.Object ? JsonProperties.String(defaults, "severity") : null,
            constraints.TryGetProperty("order", out var order) ? JsonProperties.Strings(order) : [],
            builtIns);
    }

    private static DislConstraintRule Rule(DislSpecification specification, JsonElement rule, string pointer)
    {
        var id = JsonProperties.String(rule, "id") ?? pointer;
        if (DislFieldReader.NotAnObject(rule) is { } malformed) return new DislConstraintRule { Pointer = pointer, Id = id, Code = id, NotAnObject = malformed };

        var severity = rule.TryGetProperty("severity", out var declaredSeverity) ? declaredSeverity : default;
        return new DislConstraintRule
        {
            Pointer = pointer,
            Id = id,
            Code = JsonProperties.String(rule, "code") ?? id,
            Kind = JsonProperties.String(rule, "kind"),
            Enabled = !rule.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            Enforcement = JsonProperties.String(rule, "enforcement"),
            Timing = JsonProperties.StringOrStrings(rule, "timing"),
            Scope = JsonProperties.StringOrStrings(rule, "scope"),
            ForEach = DislFieldReader.OptionalExpression(specification, rule, pointer, "forEach"),
            When = DislFieldReader.OptionalExpression(specification, rule, pointer, "when"),
            Rule = DislFieldReader.OptionalExpression(specification, rule, pointer, "rule"),
            SeverityText = severity.ValueKind == JsonValueKind.String ? severity.GetString() : null,
            Severity = severity.ValueKind is JsonValueKind.Undefined or JsonValueKind.String ? null : DislFieldReader.Expression(specification, severity, JsonPointer.Append(pointer, "severity")),
            Message = DislFieldReader.OptionalMessage(specification, rule, pointer, "message"),
            Location = DislFieldReader.OptionalExpression(specification, rule, pointer, "location"),
            Subject = DislFieldReader.OptionalExpression(specification, rule, pointer, "subject"),
            Target = rule.TryGetProperty("target", out var target) && !(target.ValueKind == JsonValueKind.String && target.GetString() == "self") ? DislFieldReader.OptionalExpression(specification, rule, pointer, "target") : null,
        };
    }

    private static DislBuiltIn BuiltIn(DislSpecification specification, JsonElement setting, string pointer)
    {
        var code = setting.TryGetProperty("code", out var declared) ? declared : default;
        return new DislBuiltIn(
            !setting.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            JsonProperties.String(setting, "severity"),
            JsonProperties.String(setting, "oncePerGroup"),
            code.ValueKind == JsonValueKind.String ? code.GetString() : null,
            code.ValueKind == JsonValueKind.Object ? DislFieldReader.Expression(specification, code, JsonPointer.Append(pointer, "code")) : null,
            DislFieldReader.OptionalMessage(specification, setting, pointer, "message"),
            DislFieldReader.OptionalMessage(specification, setting, pointer, "refusal"));
    }
}
