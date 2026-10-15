using System.Globalization;
using System.Text;

namespace EtAlii.Adp.Documents;

/// <summary>
/// What a string, an IRI or the local part of a name means once its escapes are decoded, as
/// Turtle writes them: <c>\t</c>, <c>\n</c> and the other single-letter escapes, and the numeric
/// <c>\uXXXX</c> and <c>\UXXXXXXXX</c>.
/// </summary>
/// <remarks>
/// An escape this does not know stands for the character after its backslash, and a numeric
/// escape without its hex digits stands for its letter, so a malformed escape is read rather than
/// refused.
/// </remarks>
public static class RdfEscapes
{
    /// <summary>A string token's value: its quotes, one or three, stripped, and its escapes decoded.</summary>
    public static string String(string raw)
    {
        var quote = raw[0];
        var isLong = raw.Length >= 6 && raw[1] == quote && raw[2] == quote;
        var inner = isLong ? raw[3..^3] : raw[1..^1];

        if (!inner.Contains('\\'))
        {
            return inner;
        }

        var builder = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] != '\\' || i + 1 >= inner.Length)
            {
                builder.Append(inner[i]);
                continue;
            }

            i++;
            switch (inner[i])
            {
                case 't': builder.Append('\t'); break;
                case 'b': builder.Append('\b'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 'f': builder.Append('\f'); break;
                case 'u' or 'U':
                    var length = inner[i] == 'u' ? 4 : 8;
                    AppendCodePoint(builder, inner, ref i, length);
                    break;
                default: builder.Append(inner[i]); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>The inside of a <c>&lt;...&gt;</c> token with its numeric escapes decoded, the only escapes an IRI may carry.</summary>
    public static string Iri(string raw) => Numeric(raw[1..^1]);

    /// <summary>A prefixed name's local part with its reserved-character escapes dropped: <c>\.</c> written for <c>.</c>, and so on.</summary>
    public static string Local(string raw)
    {
        if (!raw.Contains('\\'))
        {
            return raw;
        }

        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
            }

            builder.Append(raw[i]);
        }

        return builder.ToString();
    }

    /// <summary><paramref name="value"/> with its <c>\uXXXX</c> and <c>\UXXXXXXXX</c> escapes decoded and every other character kept.</summary>
    private static string Numeric(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is 'u' or 'U')
            {
                var length = value[i + 1] == 'u' ? 4 : 8;
                i++;
                AppendCodePoint(builder, value, ref i, length);
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static void AppendCodePoint(StringBuilder builder, string source, ref int index, int digits)
    {
        if (index + digits < source.Length
            && int.TryParse(source.AsSpan(index + 1, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
            index += digits;
        }
        else
        {
            builder.Append(source[index]);
        }
    }
}
