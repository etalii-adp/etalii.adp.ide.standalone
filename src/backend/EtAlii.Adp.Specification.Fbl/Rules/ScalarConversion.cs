using System.Globalization;
using EtAlii.Adp.Specification.Fbl.Yaml;

namespace EtAlii.Adp.Specification.Fbl.Rules;

/// <summary>
/// The conversion of a value to the type its specification gives the attribute (FBL 0.5 §5.2, §7.4).
/// The type names are the specification languages' scalar types; a type this class does not know, an
/// element type among them, is not checked and the value is kept as it is.
/// </summary>
internal static class ScalarConversion
{
    /// <summary>
    /// The value a slot reads as <paramref name="type"/>, or false when it does not convert.
    /// <paramref name="style"/> is the style name of a written scalar of a yaml or json body (FBL 0.5
    /// §2.4), and null for a value that is no such scalar: text from the xml, lines and blocks
    /// families, which converts from that text as before FBL 0.5 and is kept as it is, or a collection.
    /// A quoted scalar converts to a number or a bool only when <paramref name="quoted"/> asks for it,
    /// and then from its <paramref name="written"/> text by the core schema.
    /// </summary>
    public static bool Convert(string type, object? typed, string written, string? style, bool quoted, out object? value)
    {
        value = typed;
        if (typed is null) return true;
        if (style is null) return Reads(type, typed);
        if (style is "single" or "double" && type is "int" or "number" or "bool")
        {
            if (!quoted) return false;
            typed = YamlScalars.Typed(written);
        }
        switch (type)
        {
            case "int":
                value = typed switch
                {
                    long l => l,
                    double d when double.IsInteger(d) && d is >= long.MinValue and < long.MaxValue => (long)d,
                    _ => null,
                };
                return value is not null;
            case "number":
                value = typed;
                return typed is long or double;
            case "bool":
                value = typed;
                return typed is bool;
            default:
                return Reads(type, typed);
        }
    }

    /// <summary>What a value of each type is called in a finding: "'wide' is not a whole number".</summary>
    public static string Describe(string type) => type switch
    {
        "int" => "a whole number",
        "number" => "a number",
        "bool" => "true or false",
        "date" => "a date",
        "datetime" => "a date and time",
        "time" => "a time",
        _ => $"a {type}",
    };

    /// <summary>Whether a value converts to <paramref name="type"/> as it is: typed from a tree, text from the other families.</summary>
    private static bool Reads(string type, object? value) => type switch
    {
        "number" => value is long or int or double or decimal || (value is string text && IsNumber(text)),
        "bool" => value is bool || value is "true" or "false",
        "date" => value is string text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "datetime" => value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
        "time" => value is string text && TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        _ => true,
    };

    private static bool IsNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);
}
