using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What every derivation shares: evaluating a specification's expressions, its Messages and its
/// Reasons (DISL §2.3, §2.5), and the text of a value as a form shows it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Programs are compiled once per specification, by the loader.</b> Every part the derivation reads is typed at
/// load, each Expression with the program the loader compiled at its pointer; a position that reads as an
/// Expression where the loader compiles none is refused at load, so nothing is compiled here.
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
    /// The value of the Expression at <paramref name="pointer"/> whose CEL is <paramref name="source"/> (null when it holds
    /// none) and whose compiled program is <paramref name="program"/>: a <see cref="CelError"/> when it holds none, has
    /// none, or fails.
    /// </summary>
    internal static object? Evaluate(string pointer, string? source, CelProgram? program, IReadOnlyDictionary<string, object?> variables)
    {
        if (source is null) return new CelError($"{pointer} is not an expression.");
        return program is not null ? program.Evaluate(variables) : new CelError($"No expression was compiled at {pointer}.");
    }

    /// <summary>The reason <c>behavior.reasons</c> declares as <paramref name="id"/> (§2.3), in the element context; null when it declares none or it does not apply.</summary>
    internal static string? NamedReason(DislSpecification specification, string id, IReadOnlyDictionary<string, object?> variables) =>
        specification.Behavior.Reason(id)?.Text(specification, variables);

    /// <summary>The text <c>behavior.messages</c> gives for <paramref name="key"/> (§9.1), else <paramref name="fallback"/>.</summary>
    public static string StandardMessage(DislSpecification specification, string key, IReadOnlyDictionary<string, object?> variables, string fallback) =>
        specification.Behavior.Message(key)?.Text(variables, fallback) ?? fallback;

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
