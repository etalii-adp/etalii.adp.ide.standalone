using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>Scalars as the YAML 1.2 core schema reads them, and as FBL §6.3 writes them.</summary>
/// <remarks>Public because azure-devops-pipeline's planner writes its scalars through it (spec 025 T302, by T226's rule that a type another project uses is public).</remarks>
public static partial class YamlScalars
{
    [GeneratedRegex("^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Decimal();

    [GeneratedRegex("^0o[0-7]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Octal();

    [GeneratedRegex("^0x[0-9a-fA-F]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Hexadecimal();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Float();

    [GeneratedRegex(@"^[-+]?(\.inf|\.Inf|\.INF)$|^(\.nan|\.NaN|\.NAN)$", RegexOptions.CultureInvariant)]
    private static partial Regex Special();

    [GeneratedRegex("^[0-9][0-9][0-9][0-9]-[0-9][0-9]?-[0-9][0-9]?([Tt ]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"^[-+]?([0-9][0-9_]*)?\.?[0-9_]*([eE][-+]?[0-9]+)?$|^0b[01_]+$|^[-+]?0[0-7_]+$|^[-+]?[0-9][0-9_]*(:[0-5]?[0-9])+(\.[0-9_]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Yaml11Number();

    /// <summary>A plain scalar's value by the YAML 1.2 core schema: null, bool, int, float, else the string.</summary>
    public static object? Typed(string plain)
    {
        switch (plain)
        {
            case "" or "~" or "null" or "Null" or "NULL": return null;
            case "true" or "True" or "TRUE": return true;
            case "false" or "False" or "FALSE": return false;
        }
        if (Decimal().IsMatch(plain) && long.TryParse(plain, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
        if (Octal().IsMatch(plain)) return Convert.ToInt64(plain[2..], 8);
        if (Hexadecimal().IsMatch(plain)) return long.Parse(plain[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (Float().IsMatch(plain) && double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        if (Special().IsMatch(plain))
        {
            if (plain.Contains("nan", StringComparison.OrdinalIgnoreCase)) return double.NaN;
            return plain.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
        }
        return plain;
    }

    /// <summary>
    /// Whether YAML 1.1 reads <paramref name="value"/> as a number: it matches one of 1.1's number forms and holds a
    /// digit before any exponent, so that <c>e1</c>, an exponent with nothing to raise, stays a string.
    /// </summary>
    private static bool IsYaml11Number(string value) =>
        Yaml11Number().IsMatch(value) && value.TakeWhile(c => c is not ('e' or 'E')).Any(char.IsAsciiDigit);

    /// <summary>
    /// FBL §6.3: a string is plain-safe when it is not empty; has no leading or trailing whitespace;
    /// no line break or control character; does not start with an indicator; contains neither ': '
    /// nor ' #' and does not end with ':'; and reads back as the same string under the YAML 1.2 core
    /// schema and as a string under YAML 1.1, unless the attribute's type is what it would read as
    /// (<paramref name="timeTyped"/> for a date or date-time attribute).
    /// </summary>
    public static bool IsPlainSafe(string value, bool timeTyped)
    {
        if (!IsPlainSyntax(value)) return false;
        if (Typed(value) is not string) return false;
        var lower = value.ToLowerInvariant();
        if (lower is "yes" or "no" or "on" or "off" or "y" or "n" or "true" or "false" or "null" or "~") return false;
        if (IsYaml11Number(value)) return false;
        return !Timestamp().IsMatch(value) || timeTyped;
    }

    /// <summary>
    /// Whether YAML's syntax lets <paramref name="value"/> be written plain, whatever a schema then
    /// types it as: the plain-safe test without its reading back as a string (FBL 0.5 §6.3), for an
    /// attribute read as text whose <c>style</c> is <c>plain</c>.
    /// </summary>
    public static bool IsPlainSyntax(string value)
    {
        if (value.Length == 0) return false;
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])) return false;
        if (value.Any(c => c is '\n' or '\r' || char.IsControl(c))) return false;
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0])) return false;
        return !value.Contains(": ", StringComparison.Ordinal) && !value.Contains(" #", StringComparison.Ordinal) && !value.EndsWith(':');
    }

    /// <summary>
    /// Whether <paramref name="value"/> can be written in plain style, which a kept plain style and an
    /// attribute's <c>style: plain</c> ask for (FBL §6.3): plain-safe, or a <c>-</c> followed by a
    /// non-space that is otherwise plain-safe, which YAML reads as a plain string rather than a sequence
    /// entry (<c>-3200-01</c>), as long as it reads back as that string under both schemas.
    /// </summary>
    public static bool IsPlainWritable(string value, bool timeTyped)
    {
        if (IsPlainSafe(value, timeTyped)) return true;
        if (value.Length < 2 || value[0] != '-' || char.IsWhiteSpace(value[1]) || value.StartsWith("---", StringComparison.Ordinal)) return false;
        if (!IsPlainSafe("x" + value[1..], timeTyped)) return false;
        return Typed(value) is string && !IsYaml11Number(value);
    }

    public static string DoubleQuoted(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                case '\r': builder.Append("\\r"); break;
                default:
                    if (char.IsControl(c)) builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }

    public static string SingleQuoted(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
