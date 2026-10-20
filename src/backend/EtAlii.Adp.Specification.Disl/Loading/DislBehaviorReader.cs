using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads what <c>behavior</c> declares besides its operations (DISL §9) into <see cref="DislBehavior"/>: the hooks, the
/// messages, the named reasons and the deletion and retype policies, every expression with the program the loader
/// compiled at its pointer.
/// </summary>
/// <remarks>
/// Reading never refuses: what the runtime threw on, skipped or read as absent when it read the JSON at run time is
/// read as a record that does the same when it is used.
/// </remarks>
internal static class DislBehaviorReader
{
    public static DislBehavior Read(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("behavior", out var behavior)) return DislBehavior.None;
        if (DislFieldReader.NotAnObject(behavior) is { } malformed) return DislBehavior.None with { Malformed = malformed };

        var messages = new Dictionary<string, DislMessage>(StringComparer.Ordinal);
        string? messagesMalformed = null;
        if (behavior.TryGetProperty("messages", out var declaredMessages))
        {
            messagesMalformed = DislFieldReader.NotAnObject(declaredMessages);
            if (messagesMalformed is null)
            {
                foreach (var message in declaredMessages.EnumerateObject()) messages[message.Name] = DislFieldReader.Message(specification, message.Value, DislJson.Pointer("/behavior/messages", message.Name));
            }
        }

        var reasons = new Dictionary<string, DislReason>(StringComparer.Ordinal);
        string? reasonsMalformed = null;
        if (behavior.TryGetProperty("reasons", out var declaredReasons))
        {
            reasonsMalformed = DislFieldReader.NotAnObject(declaredReasons);
            if (reasonsMalformed is null)
            {
                foreach (var reason in declaredReasons.EnumerateObject()) reasons[reason.Name] = DislFieldReader.Reason(specification, reason.Value, DislJson.Pointer("/behavior/reasons", reason.Name));
            }
        }

        var depth = 100;
        string? depthMalformed = null;
        if (behavior.TryGetProperty("maxHookDepth", out var declaredDepth))
        {
            depthMalformed = DislFieldReader.NotANumber(declaredDepth);
            if (depthMalformed is null && declaredDepth.TryGetInt32(out var value)) depth = value;
        }

        return new DislBehavior(
            Hooks(specification, behavior),
            depth,
            depthMalformed,
            messages,
            messagesMalformed,
            reasons,
            reasonsMalformed,
            [.. DislJson.Members(behavior, "deletion").Select(policy => (policy.Name, Deletion(specification, policy.Value, DislJson.Pointer("/behavior/deletion", policy.Name))))],
            [.. DislJson.Members(behavior, "retype").Select(policy => (policy.Name, Retype(specification, policy.Value, DislJson.Pointer("/behavior/retype", policy.Name))))],
            null);
    }

    /// <summary>The hooks that are objects: one that is not runs on no event.</summary>
    private static List<DislHook> Hooks(DislSpecification specification, JsonElement behavior)
    {
        var hooks = new List<DislHook>();
        if (!behavior.TryGetProperty("hooks", out var declared) || declared.ValueKind != JsonValueKind.Array) return hooks;
        var index = 0;
        foreach (var hook in declared.EnumerateArray())
        {
            var pointer = DislJson.Pointer("/behavior/hooks", index++);
            if (hook.ValueKind != JsonValueKind.Object) continue;

            long order = 0;
            string? orderMalformed = null;
            if (hook.TryGetProperty("order", out var declaredOrder))
            {
                orderMalformed = DislFieldReader.NotANumber(declaredOrder);
                if (orderMalformed is null && declaredOrder.TryGetInt64(out var value)) order = value;
            }
            hooks.Add(new DislHook(
                pointer,
                DislJson.String(hook, "id") ?? pointer,
                DislJson.Strings(hook, "on"),
                DislJson.String(hook, "phase") ?? "after",
                DislJson.Strings(hook, "for"),
                DislJson.Strings(hook, "attribute"),
                order,
                orderMalformed,
                hook.TryGetProperty("when", out var when) ? DislFieldReader.Expression(specification, when, DislJson.Pointer(pointer, "when")) : null,
                hook.TryGetProperty("actions", out var actions) ? DislActionReader.List(specification, actions, DislJson.Pointer(pointer, "actions")) : null));
        }
        return hooks;
    }

    private static DislDeletionPolicy Deletion(DislSpecification specification, JsonElement policy, string pointer)
    {
        if (DislFieldReader.NotAnObject(policy) is { } malformed) return new DislDeletionPolicy(null, null, null, null, malformed);
        return new DislDeletionPolicy(
            DislJson.String(policy, "children"),
            DislJson.String(policy, "relations"),
            DislJson.String(policy, "references"),
            policy.TryGetProperty("confirm", out var confirm) ? Confirm(specification, confirm, DislJson.Pointer(pointer, "confirm")) : null,
            null);
    }

    private static DislDeletionConfirm Confirm(DislSpecification specification, JsonElement confirm, string pointer)
    {
        // A plain Message is asked every time (§9.5).
        if (confirm.ValueKind != JsonValueKind.Object || confirm.TryGetProperty("cel", out _) || !confirm.TryGetProperty("message", out var message))
        {
            return new DislDeletionConfirm.Always(DislFieldReader.Message(specification, confirm, pointer));
        }

        long threshold = 0;
        string? thresholdMalformed = null;
        if (confirm.TryGetProperty("threshold", out var declared))
        {
            thresholdMalformed = DislFieldReader.NotANumber(declared);
            if (thresholdMalformed is null && declared.TryGetInt64(out var limit)) threshold = limit;
        }
        return new DislDeletionConfirm.Asked(
            confirm.TryGetProperty("count", out var count) ? DislFieldReader.Expression(specification, count, DislJson.Pointer(pointer, "count")) : null,
            threshold,
            thresholdMalformed,
            confirm.TryGetProperty("when", out var when) ? DislFieldReader.Expression(specification, when, DislJson.Pointer(pointer, "when")) : null,
            Text("title"),
            DislFieldReader.Message(specification, message, DislJson.Pointer(pointer, "message")),
            Text("confirmLabel"),
            !confirm.TryGetProperty("danger", out var danger) || danger.ValueKind != JsonValueKind.False);

        DislMessage? Text(string name) =>
            confirm.TryGetProperty(name, out var text) ? DislFieldReader.Message(specification, text, DislJson.Pointer(pointer, name)) : null;
    }

    private static DislRetypePolicy Retype(DislSpecification specification, JsonElement policy, string pointer)
    {
        var mappingAt = DislJson.Pointer(pointer, "attributeMapping");
        return new DislRetypePolicy(
            policy.ValueKind == JsonValueKind.Object,
            DislJson.Strings(policy, "to"),
            [.. DislJson.Members(policy, "attributeMapping").Select(mapping => (mapping.Name, DislFieldReader.Expression(specification, mapping.Value, DislJson.Pointer(mappingAt, mapping.Name))))]);
    }
}
