using System.Collections;
using System.Globalization;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A value as a body's family read it, in its written form. YAML and JSON give numbers and
/// booleans as such and XML gives text for everything, so everything that compares or shows a
/// value goes through here and the three formats cannot differ in what a table shows.
/// </summary>
internal static class KnowledgeValues
{
    /// <summary>The value as text: a boolean as <c>true</c> or <c>false</c>, a number in its invariant form, nothing as empty.</summary>
    public static string Text(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        // A reference read as a list of one, as some families give it.
        IEnumerable list => Text(list.Cast<object?>().FirstOrDefault()),
        _ => value.ToString() ?? "",
    };

    /// <summary>Whether the value is the boolean true, however its family wrote it.</summary>
    public static bool IsTrue(object? value) => value is true || value is "true";

    /// <summary>The value as a whole number, or null when it is absent or is not one.</summary>
    public static int? Whole(object? value) => value switch
    {
        long whole when whole is >= int.MinValue and <= int.MaxValue => (int)whole,
        int whole => whole,
        double real when Math.Abs(real % 1) < double.Epsilon && real is >= int.MinValue and <= int.MaxValue => (int)real,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}
