using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Fbl.Yaml;

/// <summary>Scalars as the YAML 1.2 core schema reads them, and as FBL §6.3 writes them.</summary>
internal static partial class YamlScalars
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

    [GeneratedRegex(@"^[0-9][0-9][0-9][0-9]-[0-9][0-9]?-[0-9][0-9]?([Tt ]|$)", RegexOptions.CultureInvariant)]
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
    /// FBL §6.3: a string is plain-safe when it is not empty; has no leading or trailing whitespace;
    /// no line break or control character; does not start with an indicator; contains neither ': '
    /// nor ' #' and does not end with ':'; and reads back as the same string under the YAML 1.2 core
    /// schema and as a string under YAML 1.1, unless the attribute's type is what it would read as
    /// (<paramref name="timeTyped"/> for a date or date-time attribute).
    /// </summary>
    public static bool IsPlainSafe(string value, bool timeTyped)
    {
        if (value.Length == 0) return false;
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])) return false;
        if (value.Any(c => c is '\n' or '\r' || char.IsControl(c))) return false;
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0])) return false;
        if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal) || value.EndsWith(':')) return false;
        if (Typed(value) is not string) return false;
        var lower = value.ToLowerInvariant();
        if (lower is "yes" or "no" or "on" or "off" or "y" or "n" or "true" or "false" or "null" or "~") return false;
        if (Yaml11Number().IsMatch(value) && value.Any(char.IsAsciiDigit)) return false;
        if (Timestamp().IsMatch(value)) return timeTyped;
        return true;
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
        return Typed(value) is string && !(Yaml11Number().IsMatch(value) && value.Any(char.IsAsciiDigit));
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

    /// <summary>Decodes a double-quoted scalar's content (without its quotes), folding line breaks as YAML does.</summary>
    public static string DecodeDouble(string content)
    {
        var builder = new StringBuilder();
        var folded = Fold(content);
        for (var i = 0; i < folded.Length; i++)
        {
            var c = folded[i];
            if (c != '\\' || i + 1 >= folded.Length)
            {
                builder.Append(c);
                continue;
            }
            var e = folded[++i];
            switch (e)
            {
                case 'n': builder.Append('\n'); break;
                case 't' or '\t': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case '0': builder.Append('\0'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 'e': builder.Append('\u001b'); break;
                case 'f': builder.Append('\f'); break;
                case 'v': builder.Append('\v'); break;
                case ' ': builder.Append(' '); break;
                case '/': builder.Append('/'); break;
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case 'N': builder.Append('\u0085'); break;
                case '_': builder.Append(' '); break;
                case 'x': builder.Append(Hex(folded, ref i, 2)); break;
                case 'u': builder.Append(Hex(folded, ref i, 4)); break;
                case 'U': builder.Append(Hex(folded, ref i, 8)); break;
                default: builder.Append('\\').Append(e); break;
            }
        }
        return builder.ToString();
    }

    private static string Hex(string text, ref int i, int digits)
    {
        var available = Math.Min(digits, text.Length - i - 1);
        if (available <= 0 || !int.TryParse(text.AsSpan(i + 1, available), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) return "";
        i += available;
        return code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(code) : "";
    }

    public static string DecodeSingle(string content) => Fold(content).Replace("''", "'", StringComparison.Ordinal);

    /// <summary>Line folding of flow scalars: a line break and the whitespace around it become a space, an empty line a newline.</summary>
    public static string Fold(string content)
    {
        if (!content.Contains('\n')) return content;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder(lines[0].TrimEnd(' ', '\t'));
        var empty = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = i == lines.Length - 1 ? lines[i].TrimStart(' ', '\t') : lines[i].Trim(' ', '\t');
            if (line.Length == 0 && i < lines.Length - 1)
            {
                empty++;
                continue;
            }
            builder.Append(empty > 0 ? new string('\n', empty) : " ");
            empty = 0;
            builder.Append(line);
        }
        return builder.ToString();
    }
}
