using System.Globalization;

namespace EtAlii.Adp.Specification.Fbl.Rules;

/// <summary>
/// Whether a value as a body's family read it converts to the type its specification gives the
/// attribute (FBL §7.4). The type names are the specification languages' scalar types; a type this
/// class does not know, an element type among them, is not checked.
/// </summary>
internal static class TypedValue
{
    public static bool Reads(string type, object? value) => type switch
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
