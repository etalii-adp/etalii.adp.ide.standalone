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
/// is compiled on first use into the specification's <see cref="DislSpecification.Programs"/>, and
/// kept for as long as the specification lives.
/// </para>
/// <para>
/// <b>An expression that fails gives no value</b> (<see cref="CelError"/>), which each caller turns
/// into the position's fallback: a Message its default text, a condition false.
/// </para>
/// </remarks>
internal static class DislEvaluation
{
    private static readonly CelProgram Text = CelEnvironment.Standard().DeclareVariable("v").Compile("string(v)");

    /// <summary>
    /// The value of <paramref name="source"/> compiled in <paramref name="context"/>, for an expression the
    /// loader does not walk; a variable beyond the context's (an option's <c>item</c>) is declared as bound.
    /// </summary>
    private static object? Of(DislSpecification specification, DislContext context, string source, IReadOnlyDictionary<string, object?> variables)
    {
        var declared = DislContexts.VariablesOf(context);
        var bindings = variables.Keys.Where(name => !declared.Contains(name)).Order(StringComparer.Ordinal);
        return specification.Programs.TryGet(DislProgramKey.Of(context, source, bindings), out var program, out var problem)
            ? program.Evaluate(variables)
            : new CelError(problem);
    }

    /// <summary>
    /// The value of an Expression property (§2.5 a) at <paramref name="pointer"/>: a bare string or the
    /// <c>cel</c> of an object, compiled by the loader, or in <paramref name="context"/> when it is not.
    /// </summary>
    public static object? Expression(DislSpecification specification, JsonElement json, string pointer, DislContext context, IReadOnlyDictionary<string, object?> variables)
    {
        (string? source, string at) = DislFieldReader.Source(json, pointer);
        return Evaluate(specification, pointer, source, source is null ? null : specification.ExpressionAt(at)?.Program, context, variables);
    }

    /// <summary>
    /// The value of the Expression at <paramref name="pointer"/> whose CEL is <paramref name="source"/> (null when it holds
    /// none) and whose compiled program is <paramref name="program"/>, or compiled in <paramref name="context"/> when the
    /// loader compiled none; what both an Expression read from the JSON and one typed at load (<see cref="DislExpressionField"/>) give.
    /// </summary>
    internal static object? Evaluate(DislSpecification specification, string pointer, string? source, CelProgram? program, DislContext context, IReadOnlyDictionary<string, object?> variables)
    {
        if (source is null) return new CelError($"{pointer} is not an expression.");
        return program is not null ? program.Evaluate(variables) : Of(specification, context, source, variables);
    }

    /// <summary>Whether the condition at <paramref name="pointer"/> holds; a condition that fails does not.</summary>
    public static bool Holds(DislSpecification specification, JsonElement json, string pointer, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
        Expression(specification, json, pointer, context, variables) is true;

    /// <summary>
    /// The text of a Message (§2.3): a string as it is, a locale map's English or first text, the value
    /// of a <c>{cel}</c> object; <paramref name="fallback"/> when it is absent or fails.
    /// </summary>
    public static string Message(DislSpecification specification, JsonElement json, string pointer, DislContext context, IReadOnlyDictionary<string, object?> variables, string fallback) =>
        DislFieldReader.Message(specification, json, pointer).Text(specification, context, variables, fallback);

    /// <summary>
    /// The first Reason of <paramref name="reasons"/> that applies (§2.3): a Message always does, an
    /// object while its <c>when</c> holds, a <c>{reason: id}</c> while the named reason's does.
    /// </summary>
    public static string? FirstReason(DislSpecification specification, JsonElement reasons, string pointer, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
        DislReason.First(DislFieldReader.Reasons(specification, reasons, pointer), specification, context, variables);

    /// <summary>One Reason (§2.3): a Message, an object while its <c>when</c> holds, a <c>{reason: id}</c> while the named one's does; null when it does not apply.</summary>
    internal static string? Reason(DislSpecification specification, JsonElement reason, string pointer, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
        DislFieldReader.Reason(specification, reason, pointer).Text(specification, context, variables);

    /// <summary>The reason <c>behavior.reasons</c> declares as <paramref name="id"/> (§2.3), in the element context; null when it declares none or it does not apply.</summary>
    internal static string? NamedReason(DislSpecification specification, string id, IReadOnlyDictionary<string, object?> variables) =>
        specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("reasons", out var all) && all.TryGetProperty(id, out var declared)
            ? Reason(specification, declared, DislJson.Pointer("/behavior/reasons", id), DislContext.Element, variables)
            : null;

    /// <summary>The text <c>behavior.messages</c> gives for <paramref name="key"/> (§9.1), else <paramref name="fallback"/>.</summary>
    public static string StandardMessage(DislSpecification specification, string key, IReadOnlyDictionary<string, object?> variables, string fallback) =>
        specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("messages", out var messages) && messages.TryGetProperty(key, out var message)
            ? Message(specification, message, DislJson.Pointer("/behavior/messages", key), DislContext.Element, variables, fallback)
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

    /// <summary>A locale map's text (§2.3): its English, else its first English variant's, else its first; null when it has none.</summary>
    internal static string? Localized(IEnumerable<(string Tag, string? Text)> locales)
    {
        var list = locales.Where(locale => locale.Text is not null).ToList();
        return list.FirstOrDefault(locale => locale.Tag == "en").Text ?? list.FirstOrDefault(locale => locale.Tag.StartsWith("en-", StringComparison.Ordinal)).Text ?? list.FirstOrDefault().Text;
    }
}
