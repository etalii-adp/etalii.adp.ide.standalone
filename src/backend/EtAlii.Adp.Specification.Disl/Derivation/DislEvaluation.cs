using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What every derivation shares: evaluating a specification's expressions, its Messages and its
/// Reasons (DISL §2.3, §2.5), and the text of a value as a form shows it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Programs are compiled once per specification.</b> An expression the loader compiled is taken
/// from <see cref="DislSpecification.ExpressionAt"/>; one it does not walk, inside an <c>x-</c> key,
/// is compiled on first use and kept beside the specification for as long as it lives.
/// </para>
/// <para>
/// <b>An expression that fails gives no value</b> (<see cref="CelError"/>), which each caller turns
/// into the position's fallback: a Message its default text, a condition false.
/// </para>
/// </remarks>
internal static class DislEvaluation
{
    private static readonly ConditionalWeakTable<DislSpecification, ConcurrentDictionary<(string Context, string Source, string Bindings), CelProgram>> Compiled = [];

    private static readonly CelProgram Text = CelEnvironment.Standard().DeclareVariable("v").Compile("string(v)");

    /// <summary>
    /// The value of <paramref name="source"/> compiled in <paramref name="context"/>, for an expression the
    /// loader does not walk; a variable beyond the context's (an option's <c>item</c>) is declared as bound.
    /// </summary>
    private static object? Of(DislSpecification specification, string context, string source, IReadOnlyDictionary<string, object?> variables)
    {
        var programs = Compiled.GetValue(specification, _ => new ConcurrentDictionary<(string, string, string), CelProgram>());
        var declared = DislContexts.VariablesOf(context);
        var bindings = string.Join(",", variables.Keys.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal));
        CelProgram program;
        try
        {
            program = programs.GetOrAdd((context, source, bindings), key =>
                specification.Environment(key.Context, key.Bindings.Length == 0 ? [] : key.Bindings.Split(',')).Compile(key.Source));
        }
        catch (CelException e)
        {
            return new CelError(e.Message);
        }
        return program.Evaluate(variables);
    }

    /// <summary>
    /// The value of an Expression property (§2.5 a) at <paramref name="pointer"/>: a bare string or the
    /// <c>cel</c> of an object, compiled by the loader, or in <paramref name="context"/> when it is not.
    /// </summary>
    public static object? Expression(DislSpecification specification, JsonElement json, string pointer, string context, IReadOnlyDictionary<string, object?> variables)
    {
        (string? source, string at) = json.ValueKind switch
        {
            JsonValueKind.String => (json.GetString(), pointer),
            JsonValueKind.Object when DislJson.String(json, "cel") is { } cel => (cel, DislJson.Pointer(pointer, "cel")),
            _ => (null, pointer),
        };
        if (source is null) return new CelError($"{pointer} is not an expression.");
        return specification.ExpressionAt(at) is { } compiled ? compiled.Program.Evaluate(variables) : Of(specification, context, source, variables);
    }

    /// <summary>Whether the condition at <paramref name="pointer"/> holds; a condition that fails does not.</summary>
    public static bool Holds(DislSpecification specification, JsonElement json, string pointer, string context, IReadOnlyDictionary<string, object?> variables) =>
        Expression(specification, json, pointer, context, variables) is true;

    /// <summary>
    /// The text of a Message (§2.3): a string as it is, a locale map's English or first text, the value
    /// of a <c>{cel}</c> object; <paramref name="fallback"/> when it is absent or fails.
    /// </summary>
    public static string Message(DislSpecification specification, JsonElement json, string pointer, string context, IReadOnlyDictionary<string, object?> variables, string fallback) =>
        json.ValueKind switch
        {
            JsonValueKind.String => json.GetString()!,
            JsonValueKind.Object when json.TryGetProperty("cel", out _) => Expression(specification, json, pointer, context, variables) switch
            {
                string text => text,
                CelMap locales => Localized(locales.Select(entry => (entry.Key, entry.Value as string))) ?? fallback,
                _ => fallback,
            },
            JsonValueKind.Object => Localized(json.EnumerateObject().Select(entry => (entry.Name, entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null))) ?? fallback,
            _ => fallback,
        };

    /// <summary>
    /// The first Reason of <paramref name="reasons"/> that applies (§2.3): a Message always does, an
    /// object while its <c>when</c> holds, a <c>{reason: id}</c> while the named reason's does.
    /// </summary>
    public static string? FirstReason(DislSpecification specification, JsonElement reasons, string pointer, string context, IReadOnlyDictionary<string, object?> variables)
    {
        if (reasons.ValueKind != JsonValueKind.Array) return null;
        var index = 0;
        foreach (var reason in reasons.EnumerateArray())
        {
            var at = DislJson.Pointer(pointer, index++);
            if (Reason(specification, reason, at, context, variables) is { } text) return text;
        }
        return null;
    }

    private static string? Reason(DislSpecification specification, JsonElement reason, string pointer, string context, IReadOnlyDictionary<string, object?> variables)
    {
        if (reason.ValueKind == JsonValueKind.Object && DislJson.String(reason, "reason") is { } id)
        {
            // A named reason is declared once under behavior.reasons, in the element context.
            var named = DislJson.Pointer("/behavior/reasons", id);
            return specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("reasons", out var all) && all.TryGetProperty(id, out var declared)
                ? Reason(specification, declared, named, DislContexts.Element, variables)
                : null;
        }
        if (reason.ValueKind == JsonValueKind.Object && reason.TryGetProperty("message", out var message))
        {
            if (reason.TryGetProperty("when", out var when) && !Holds(specification, when, DislJson.Pointer(pointer, "when"), context, variables)) return null;
            return Message(specification, message, DislJson.Pointer(pointer, "message"), context, variables, "");
        }
        return Message(specification, reason, pointer, context, variables, "");
    }

    /// <summary>The text <c>behavior.messages</c> gives for <paramref name="key"/> (§9.1), else <paramref name="fallback"/>.</summary>
    public static string StandardMessage(DislSpecification specification, string key, IReadOnlyDictionary<string, object?> variables, string fallback) =>
        specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("messages", out var messages) && messages.TryGetProperty(key, out var message)
            ? Message(specification, message, DislJson.Pointer("/behavior/messages", key), DislContexts.Element, variables, fallback)
            : fallback;

    /// <summary>A CEL value as text: a string as it is, any other value as CEL's <c>string()</c> writes it.</summary>
    public static string TextOf(object? value) => value switch
    {
        null => "",
        string text => text,
        long or double or bool => Text.Evaluate(new Dictionary<string, object?> { ["v"] = value }) as string ?? "",
        DislElement element => element.Id,
        _ => value.ToString() ?? "",
    };

    private static string? Localized(IEnumerable<(string Tag, string? Text)> locales)
    {
        var list = locales.Where(locale => locale.Text is not null).ToList();
        return list.FirstOrDefault(locale => locale.Tag == "en").Text ?? list.FirstOrDefault(locale => locale.Tag.StartsWith("en-", StringComparison.Ordinal)).Text ?? list.FirstOrDefault().Text;
    }
}
