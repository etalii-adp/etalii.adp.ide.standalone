using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The line-splicing helpers the three writers share: locating a key inside a range, reading
/// indentation from what is already there, and telling the two syntaxes apart.
/// </summary>
/// <remarks>
/// A write invalidates the model that located it - line numbers after a splice have moved -
/// so callers re-parse rather than adjusting ranges by hand, which is cheap and removes a whole
/// class of off-by-one bug. Every helper here reads; only the writers splice.
/// </remarks>
internal static class DatabricksSplices
{
    /// <summary>
    /// Whether the document is written in JSON's flow syntax - which the pipeline settings file
    /// is - rather than block YAML. The two need different lines spliced in: a JSON array grows
    /// an element-plus-comma, a YAML sequence grows a dash entry.
    /// </summary>
    public static bool IsJson(DatabricksDocument document) =>
        document.Lines.FirstOrDefault(line => !line.IsBlank)?.Text.TrimStart().StartsWith('{') == true;

    /// <summary>
    /// The first line inside <paramref name="range"/> whose key is <paramref name="key"/>, dash
    /// prefixes and either syntax's quoting tolerated; -1 when the range has none.
    /// </summary>
    public static int FindKey(DatabricksDocument document, LineRange range, string key)
    {
        for (var i = range.Start; i <= range.End && i < document.Lines.Count; i++)
        {
            var trimmed = document.Lines[i].Text.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..];
            }

            if (trimmed.StartsWith($"{key}:", StringComparison.Ordinal)
                || trimmed.StartsWith($"\"{key}\":", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The leading whitespace of a line's text.</summary>
    public static string Indent(string text) => text[..(text.Length - text.TrimStart().Length)];

    /// <summary>
    /// The indentation the keys inside a range already use - copied from the file rather than
    /// imposed, so a document written with four spaces stays written with four spaces.
    /// </summary>
    public static string KeyIndentWithin(DatabricksDocument document, LineRange range)
    {
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
        return Indent(document.Lines[range.Start].Text) + "  ";
    }

    /// <summary>
    /// Quotes a YAML value only where the syntax needs it, so an ordinary value stays unquoted
    /// and a document does not sprout quotation marks it never had.
    /// </summary>
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
            value.StartsWith('[');

        return needsQuoting
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
