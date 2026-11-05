using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A budget of <c>language.limits.budgets</c> (DISL §3.2.1), typed at load: what it counts, its limit, whether and in
/// which order it cuts, and its notice, withheld edits and finding.
/// </summary>
/// <param name="Id">The budget's id.</param>
/// <param name="Types">The types a <c>count</c> measure counts; empty for a <c>cel</c> one.</param>
/// <param name="Measure">A <c>cel</c> measure, in the budget context; null for a <c>count</c> one.</param>
/// <param name="Max">The limit; the budget is exceeded while the measure is greater.</param>
/// <param name="Truncate">Whether the budget cuts what is drawn.</param>
/// <param name="Order">The sort key per candidate, or null for persistence order.</param>
/// <param name="Unit">The group key per candidate, or null for each candidate its own unit.</param>
/// <param name="Notice">Whether the notice <c>budget:&lt;id&gt;</c> is shown while the budget truncates.</param>
/// <param name="NoticeText">The notice's text, in the chrome context; null for the runtime's sentence.</param>
/// <param name="NoticeSeverity">The notice's severity.</param>
/// <param name="NoticePosition">Where the notice is shown, or null for the host's place.</param>
/// <param name="Withhold">What is refused while the budget is exceeded: <c>model</c>, <c>all</c> or <c>none</c>.</param>
/// <param name="WithholdReason">The refusal shown for a withheld edit, in the element context; null for the runtime's sentence.</param>
/// <param name="FindingSeverity">The severity of the diagram-level finding while the budget is exceeded; null when it declares none.</param>
/// <param name="FindingMessage">That finding's message, in the constraint context.</param>
internal sealed record DislBudget(
    string Id,
    IReadOnlyList<string> Types,
    DislExpressionField? Measure,
    long Max,
    bool Truncate,
    DislExpressionField? Order,
    DislExpressionField? Unit,
    bool Notice,
    DislMessage? NoticeText,
    string NoticeSeverity,
    string? NoticePosition,
    string Withhold,
    DislReason? WithholdReason,
    string? FindingSeverity,
    DislMessage? FindingMessage);

/// <summary>Reads <c>language.limits.budgets</c> (DISL §3.2.1) into <see cref="DislBudget"/>s, in declaration order.</summary>
internal static class DislBudgetReader
{
    public static IReadOnlyList<DislBudget> Read(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("language", out var language) || language.ValueKind != JsonValueKind.Object) return [];
        if (!language.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Object) return [];
        return [.. DislJson.Members(limits, "budgets").Where(budget => budget.Value.ValueKind == JsonValueKind.Object).Select(budget =>
            Budget(specification, budget.Name, budget.Value, JsonPointer.Append("/language/limits/budgets", budget.Name)))];
    }

    private static DislBudget Budget(DislSpecification specification, string id, JsonElement json, string pointer)
    {
        var measure = json.TryGetProperty("measure", out var declared) ? declared : default;
        var types = measure.ValueKind == JsonValueKind.Object && measure.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Array
            ? count.EnumerateArray().Where(type => type.ValueKind == JsonValueKind.String).Select(type => type.GetString()!).ToList()
            : [];
        var cel = measure.ValueKind == JsonValueKind.Object && measure.TryGetProperty("cel", out _)
            ? DislFieldReader.Expression(specification, measure, JsonPointer.Append(pointer, "measure"))
            : null;
        var max = json.TryGetProperty("max", out var limit) && limit.TryGetInt64(out var value) ? value : long.MaxValue;
        var truncate = json.TryGetProperty("truncate", out var cuts) && cuts.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? cuts.GetBoolean()
            : cel is null;

        var notice = json.TryGetProperty("notice", out var declaredNotice) ? declaredNotice : default;
        var shown = notice.ValueKind != JsonValueKind.False;
        var text = notice.ValueKind == JsonValueKind.Object && notice.TryGetProperty("text", out var written)
            ? DislFieldReader.Message(specification, written, JsonPointer.Append(JsonPointer.Append(pointer, "notice"), "text"))
            : null;
        var severity = notice.ValueKind == JsonValueKind.Object ? JsonProperties.String(notice, "severity") ?? "info" : "info";
        var position = notice.ValueKind == JsonValueKind.Object ? JsonProperties.String(notice, "position") : null;

        var withhold = json.TryGetProperty("withhold", out var declaredWithhold) ? declaredWithhold : default;
        var edits = withhold.ValueKind == JsonValueKind.Object ? JsonProperties.String(withhold, "edits") ?? "model" : "model";
        var reason = withhold.ValueKind == JsonValueKind.Object && withhold.TryGetProperty("reason", out var because)
            ? DislFieldReader.Reason(specification, because, JsonPointer.Append(JsonPointer.Append(pointer, "withhold"), "reason"))
            : null;

        var finding = json.TryGetProperty("finding", out var declaredFinding) && declaredFinding.ValueKind == JsonValueKind.Object ? declaredFinding : default;
        var findingSeverity = finding.ValueKind == JsonValueKind.Object ? JsonProperties.String(finding, "severity") ?? "info" : null;
        var findingMessage = finding.ValueKind == JsonValueKind.Object && finding.TryGetProperty("message", out var message)
            ? DislFieldReader.Message(specification, message, JsonPointer.Append(JsonPointer.Append(pointer, "finding"), "message"))
            : null;

        return new DislBudget(
            id,
            types,
            cel,
            max,
            truncate,
            DislFieldReader.OptionalExpression(specification, json, pointer, "order"),
            DislFieldReader.OptionalExpression(specification, json, pointer, "unit"),
            shown,
            text,
            severity,
            position,
            edits,
            reason,
            findingSeverity,
            findingMessage);
    }
}
