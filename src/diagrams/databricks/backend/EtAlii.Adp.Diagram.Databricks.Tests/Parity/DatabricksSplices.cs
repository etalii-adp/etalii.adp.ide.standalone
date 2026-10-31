using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The line-splicing helpers the three writers share: locating a key inside a range, reading
/// indentation from what is already there, and telling the two syntaxes apart.
/// </summary>
/// <remarks>
/// A write invalidates the model that located it - line numbers after a splice have moved -
/// so callers re-parse rather than adjusting ranges by hand, which is cheap and removes a whole
/// class of off-by-one bug. Every helper here reads; only the writers splice.
/// <para>
/// <b>The oracle</b>: the module's hand-written code, moved here as it was, its namespace aside, from the product code when
/// databricks-disl-fbl task 12 planned every edit through the binding, so that <see cref="DatabricksDerivedOperationsTests"/>
/// can compare what the commands write with what the module wrote by hand.
/// </para>
/// </remarks>
internal static partial class DatabricksSplices
{
    /// <summary>
    /// The refusal of an edit into a list written on one line (databricks-disl-fbl finding B12): a
    /// splice of whole lines cannot add to it or take from it without breaking the file, so the edit
    /// is refused before anything is written.
    /// </summary>
    public const string OneLineListRefusal = "This list is written on one line, which this diagram cannot change yet; edit it in the text editor.";

    /// <summary>
    /// Whether the document is written in JSON's flow syntax - which the pipeline settings file
    /// is - rather than block YAML. The two need different lines spliced in: a JSON array grows
    /// an element-plus-comma, a YAML sequence grows a dash entry.
    /// </summary>
    public static bool IsJson(LineDocument document) =>
        document.Lines.FirstOrDefault(line => !line.IsBlank)?.Text.TrimStart().StartsWith('{') == true;

    /// <summary>
    /// The line inside <paramref name="range"/> that holds <paramref name="key"/> as one of the
    /// entry's own keys, either syntax's quoting tolerated; -1 when the entry has none.
    /// </summary>
    /// <remarks>
    /// Only a key written at the entry's own key column counts (finding B10): a key of the same name
    /// in a mapping nested inside the entry - the <c>task_key</c> of a <c>for_each_task</c>'s inner
    /// task, written before the outer task's own - is another entry's.
    /// </remarks>
    public static int FindKey(LineDocument document, LineRange range, string key)
    {
        var column = OwnKeyColumn(document, range);
        for (var i = range.Start; i <= range.End && i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            var at = KeyColumn(text);
            if (at != column)
            {
                continue;
            }

            var written = text[at..];
            if (written.StartsWith($"{key}:", StringComparison.Ordinal)
                || written.StartsWith($"\"{key}\":", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The leading whitespace of a line's text.</summary>
    public static string Indent(string text) => text[..^text.TrimStart().Length];

    /// <summary>
    /// The indentation the keys inside a range already use - copied from the file rather than
    /// imposed, so a document written with four spaces stays written with four spaces.
    /// </summary>
    /// <remarks>
    /// A sequence entry's keys stand where its first key does, after the dash, whatever its first key
    /// holds: the line after a first key that opens a nested mapping is that mapping's (finding B10).
    /// </remarks>
    public static string KeyIndentWithin(LineDocument document, LineRange range)
    {
        var first = document.Lines[range.Start].Text;
        if (IsSequenceEntry(first))
        {
            return new string(' ', KeyColumn(first));
        }

        for (var i = range.Start + 1; i <= range.End && i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return Indent(text);
            }
        }

        // A one-line construct has no neighbour to copy, so its own indentation plus one step
        // puts the new key under it.
        return Indent(first) + "  ";
    }

    /// <summary>
    /// Where a key the entry in <paramref name="range"/> does not have yet goes: directly under the
    /// entry's first line - or, where that line's key opens a nested mapping, after that mapping, so the
    /// new key is the entry's own rather than the nested one's (finding B10).
    /// </summary>
    public static int NewKeyLine(LineDocument document, LineRange range)
    {
        var first = document.Lines[range.Start].Text;
        if (!IsSequenceEntry(first) || !OpensBlock(first[KeyColumn(first)..]))
        {
            return range.Start + 1;
        }

        var column = KeyColumn(first);
        for (var i = range.Start + 1; i <= range.End && i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith('#') && KeyColumn(text) <= column)
            {
                return i;
            }
        }

        return range.End + 1;
    }

    /// <summary>
    /// Whether the list an entry holds under <paramref name="key"/>, whose items are
    /// <paramref name="items"/>, is written on one line (finding B12): a flow sequence such as
    /// <c>tasks: []</c> or <c>depends_on: [{task_key: a}]</c>, or a JSON array whose items share a line
    /// with each other or with the entry. Such a list has no line of its own to add or remove.
    /// </summary>
    public static bool IsOneLineList(LineDocument document, LineRange owner, string key, IEnumerable<LineRange> items)
    {
        var keyLine = FindKey(document, owner, key);
        if (keyLine >= 0 && IsFlowWrittenOnItsLine(document.Lines[keyLine].Text))
        {
            return true;
        }

        var ranges = items.ToList();
        for (var i = 0; i < ranges.Count; i++)
        {
            var item = ranges[i];
            if (item.Start == owner.Start || item.Start == keyLine)
            {
                return true;
            }

            for (var j = 0; j < ranges.Count; j++)
            {
                if (j != i && ranges[j].Start <= item.End && item.Start <= ranges[j].End)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Quotes a YAML value only where the syntax needs it, so an ordinary value stays unquoted
    /// and a document does not sprout quotation marks it never had.
    /// </summary>
    /// <remarks>
    /// A value is written plain only when it reads back as the same text (finding B9): the plain-safe
    /// rule of FBL §6.3, so <c>yes</c>, <c>null</c>, <c>123</c> or a leading <c>*</c>, <c>&amp;</c>,
    /// <c>!</c>, <c>%</c>, <c>@</c> or <c>[</c> is quoted rather than typed, aliased or left unclosed.
    /// What was quoted before is quoted still.
    /// </remarks>
    public static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var needsQuoting =
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains('#', StringComparison.Ordinal) ||
            value.StartsWith('-') ||
            value.StartsWith(' ') ||
            value.EndsWith(' ') ||
            value.StartsWith('"') ||
            value.StartsWith('\'') ||
            value.StartsWith('{') ||
            value.StartsWith('[') ||
            !IsPlainSafe(value);

        return needsQuoting ? DoubleQuoted(value) : value;
    }

    /// <summary>A value always in double quotes - a JSON string, or a YAML value written quoted whatever it holds - escaped so that it reads back as the same text (finding B9).</summary>
    public static string Quoted(string value) => DoubleQuoted(value);

    [GeneratedRegex("^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Decimal();

    [GeneratedRegex("^0o[0-7]+$|^0x[0-9a-fA-F]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OctalOrHexadecimal();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$|^[-+]?(\.inf|\.Inf|\.INF)$|^(\.nan|\.NaN|\.NAN)$", RegexOptions.CultureInvariant)]
    private static partial Regex Float();

    [GeneratedRegex("^[0-9][0-9][0-9][0-9]-[0-9][0-9]?-[0-9][0-9]?([Tt ]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"^[-+]?([0-9][0-9_]*)?\.?[0-9_]*([eE][-+]?[0-9]+)?$|^0b[01_]+$|^[-+]?0[0-7_]+$|^[-+]?[0-9][0-9_]*(:[0-5]?[0-9])+(\.[0-9_]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Yaml11Number();

    /// <summary>
    /// FBL §6.3's plain-safe rule: not empty, no surrounding whitespace, no line break or control
    /// character, no leading indicator, neither <c>": "</c> nor <c>" #"</c> nor a trailing colon, and a
    /// string under both the YAML 1.2 core schema and YAML 1.1.
    /// </summary>
    private static bool IsPlainSafe(string value)
    {
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])
            || value.Any(char.IsControl)
            || "-?:,[]{}#&*!|>'\"%@`".Contains(value[0], StringComparison.Ordinal)
            || value.Contains(": ", StringComparison.Ordinal)
            || value.Contains(" #", StringComparison.Ordinal)
            || value.EndsWith(':'))
        {
            return false;
        }

        if (value.ToLowerInvariant() is "" or "~" or "null" or "true" or "false" or "yes" or "no" or "on" or "off" or "y" or "n")
        {
            return false;
        }

        if (Decimal().IsMatch(value) || OctalOrHexadecimal().IsMatch(value) || Float().IsMatch(value) || Timestamp().IsMatch(value))
        {
            return false;
        }

        return !(Yaml11Number().IsMatch(value) && value.Any(char.IsAsciiDigit));
    }

    /// <summary>A value in double quotes, with what a double-quoted scalar of YAML or a JSON string escapes.</summary>
    private static string DoubleQuoted(string value)
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
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>
    /// The column an entry's own keys are written at: after the dash of a sequence entry; under the
    /// first line of an entry that opens a block (<c>key:</c>, a JSON <c>{</c>); else the first line's.
    /// -1 when an entry that opens a block holds nothing.
    /// </summary>
    private static int OwnKeyColumn(LineDocument document, LineRange range)
    {
        var first = document.Lines[range.Start].Text;
        if (IsSequenceEntry(first) || !OpensBlock(first.TrimStart()))
        {
            return KeyColumn(first);
        }

        for (var i = range.Start + 1; i <= range.End && i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith('#'))
            {
                return KeyColumn(text);
            }
        }

        return -1;
    }

    /// <summary>Where a line's key starts: after its indentation, and after the dash and its spaces of a sequence entry.</summary>
    private static int KeyColumn(string text)
    {
        var column = text.Length - text.TrimStart().Length;
        if (!IsSequenceEntry(text))
        {
            return column;
        }

        column++;
        while (column < text.Length && text[column] == ' ')
        {
            column++;
        }

        return column;
    }

    /// <summary>Whether a line is a block sequence entry: a dash followed by a space.</summary>
    private static bool IsSequenceEntry(string text) => text.TrimStart().StartsWith("- ", StringComparison.Ordinal);

    /// <summary>Whether a key's line holds no value of its own, so what follows it is nested in it: <c>key:</c>, or a JSON object or array opened at its end.</summary>
    private static bool OpensBlock(string written)
    {
        var comment = written.IndexOf(" #", StringComparison.Ordinal);
        var value = (comment >= 0 ? written[..comment] : written).TrimEnd();
        return value.EndsWith(':') || value.EndsWith('{') || value.EndsWith('[');
    }

    /// <summary>
    /// Whether a key's line holds a flow sequence that does not stop at its opening bracket - closed on
    /// that line, or with an item beside it - rather than a bracket whose items follow on lines of their own.
    /// </summary>
    private static bool IsFlowWrittenOnItsLine(string text)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var value = text[(colon + 1)..].Trim();
        if (!value.StartsWith('['))
        {
            return false;
        }

        var rest = value[1..].TrimStart();
        return rest.Length > 0 && !rest.StartsWith('#');
    }
}
