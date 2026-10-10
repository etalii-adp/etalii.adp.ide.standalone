namespace EtAlii.Adp.Documents;

/// <summary>
/// Finding and replacing lines inside a <see cref="LineDocument"/>: the mechanics a
/// line-splicing writer needs, with no opinion about what the lines say.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is here and what is deliberately not.</b> These are the operations that locate a key,
/// match existing indentation, and decide where a new entry goes. What a module writes - which
/// keys, in what order, with what values - stays in the module, because that is its format
/// rather than its file handling (file-io-centralization Requirement 3.1, and the classification
/// its design sets out).
/// </para>
/// <para>
/// <b>Eight helpers moved here, not eleven.</b> The design counted eleven identically-named
/// static helpers shared between the timeline and dependency-graph writers and read that as
/// eleven duplicates. Comparing bodies rather than names, eight are byte-identical once the
/// document type and line wrapping are set aside - the eight below, including one the design's
/// own measurement could not see because its return type is a tuple. The other four -
/// <c>InsertElement</c>, <c>RemoveElement</c>, <c>SetLabel</c>, <c>SetRow</c> - share a name and
/// differ in substance, because they encode what each format writes: a timeline element carries
/// <c>begin</c>, <c>end</c> and <c>row</c>, a dependency-graph element carries <c>x</c> and
/// <c>row</c>. Those stay where they are. A name is not evidence of sameness, which is the test
/// this spec applies everywhere else and had not yet applied to itself.
/// </para>
/// <para>
/// The YAML shape these assume - a sequence of <c>- key: value</c> items under a section key -
/// is what both consuming formats happen to be. It is not a general YAML editor and does not
/// try to be; a module whose format does not fit is not asked to adopt it (Requirement 3.3).
/// </para>
/// </remarks>
public static class LineSplice
{
    /// <summary>The item indentation used when a document has no existing entry to copy.</summary>
    public const string DefaultItemIndent = "  ";

    /// <summary>The key indentation used when a document has no existing entry to copy.</summary>
    public const string DefaultKeyIndent = "    ";

    /// <summary>The index of the line carrying <paramref name="sectionKey"/>, or -1.</summary>
    public static int FindSection(LineDocument document, string sectionKey)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var i = 0; i < document.Lines.Count; i++)
        {
            if (document.Lines[i].Text.TrimStart().StartsWith(sectionKey, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The index of the line carrying <paramref name="key"/> within <paramref name="range"/>,
    /// or -1. A key written on the item's own <c>- id:</c> line is found too.
    /// </summary>
    public static int FindKey(LineDocument document, LineRange range, string key)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var i = range.Start; i <= range.End && i < document.Lines.Count; i++)
        {
            var trimmed = document.Lines[i].Text.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..];
            }

            if (trimmed.StartsWith($"{key}:", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Replaces one key's value inside a range, keeping the line's own indentation, or adds the
    /// key when it is not there.
    /// </summary>
    public static void SetKey(LineDocument document, LineRange range, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = FindKey(document, range, key);
        if (index >= 0)
        {
            var existing = document.Lines[index].Text;
            var indent = existing[..^existing.TrimStart().Length];
            // The dash belongs to the sequence, not to the key, so a key written on the `- id:`
            // line keeps its dash and everything after the colon is replaced.
            var prefix = existing.TrimStart().StartsWith("- ", StringComparison.Ordinal) ? "- " : "";
            document.Replace(new LineRange(index, index), [$"{indent}{prefix}{key}: {value}"]);
            return;
        }

        // A key that is not there yet is added directly after the range's first line, indented to
        // match its neighbours rather than to this module's taste.
        var keyIndent = KeyIndentWithin(document, range);
        document.Insert(range.Start + 1, [$"{keyIndent}{key}: {value}"]);
    }

    /// <summary>Removes one key's line from within a range, if it is there.</summary>
    public static void RemoveKey(LineDocument document, LineRange range, string key)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = FindKey(document, range, key);
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    /// <summary>The indentation the keys inside a range already use.</summary>
    public static string KeyIndentWithin(LineDocument document, LineRange range)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var i = range.Start + 1; i <= range.End && i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text[..^text.TrimStart().Length];
            }
        }

        // A one-line element has no neighbour to copy, so the dash's own indentation plus two
        // spaces puts the new key under it - which is what the `- ` prefix occupies.
        var first = document.Lines[range.Start].Text;
        return first[..^first.TrimStart().Length] + DefaultItemIndent;
    }

    /// <summary>
    /// The item indentation, the gap after the dash, and the key indentation an existing
    /// declaration uses - or the defaults when the document has no example to copy.
    /// </summary>
    /// <remarks>
    /// The gap after the dash is copied along with the indent because a document written
    /// <c>-   id:</c> throughout and given one new entry written <c>- id:</c> is visibly ADP's
    /// work rather than the author's. Matching what is already there costs three lines and is
    /// what the author would have written.
    /// </remarks>
    public static (string ItemIndent, string DashGap, string KeyIndent) IndentOf(
        LineDocument document, IEnumerable<LineRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ranges);

        var first = ranges.Cast<LineRange?>().FirstOrDefault();
        if (first is null)
        {
            return (DefaultItemIndent, " ", DefaultKeyIndent);
        }

        var range = first.Value;
        var dash = document.Lines[range.Start].Text;
        var itemIndent = dash[..^dash.TrimStart().Length];

        var afterDash = dash.TrimStart();
        var gap = " ";
        if (afterDash.StartsWith('-'))
        {
            var rest = afterDash[1..];
            gap = rest[..^rest.TrimStart().Length];
            if (gap.Length == 0)
            {
                gap = " ";
            }
        }

        return (itemIndent, gap, KeyIndentWithin(document, range));
    }

    /// <summary>
    /// Where a new entry goes: after the last existing one, or immediately after the section key
    /// when there are none. Returns -1 when the section key is absent entirely.
    /// </summary>
    /// <remarks>
    /// A section written flow-empty - <c>elements: []</c>, which is what a fresh document from a
    /// factory says, and what a hand author may equally write - is first opened into a bare
    /// <c>elements:</c> key, because appending a block entry after a line that already carries a
    /// value would leave the key with two values and the document unparseable.
    /// </remarks>
    public static int InsertionPointFor(LineDocument document, IEnumerable<LineRange> ranges, string sectionKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ranges);

        var last = ranges.Cast<LineRange?>().LastOrDefault();
        if (last is not null)
        {
            return last.Value.End + 1;
        }

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!text.TrimStart().StartsWith(sectionKey, StringComparison.Ordinal))
            {
                continue;
            }

            var value = text.TrimStart()[sectionKey.Length..].Trim();
            if (value is "[]" or "[ ]")
            {
                var indent = text[..^text.TrimStart().Length];
                document.Replace(new LineRange(i, i), [$"{indent}{sectionKey}"]);
            }

            return i + 1;
        }

        return -1;
    }

    /// <summary>
    /// Quotes a value only where YAML needs it, so an ordinary label stays unquoted and a document
    /// does not sprout quotation marks it never had.
    /// </summary>
    /// <remarks>
    /// A value with a line break - LF, CRLF or a lone CR - is always double-quoted, with each break
    /// written as its YAML escape (<c>\n</c>, <c>\r</c>). Left plain, the text after the break would
    /// land at column 0 as a line of its own, and the document would no longer parse. A value without
    /// a line break is written exactly as before.
    /// </remarks>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

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
            StartsWithIndicator(value) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal);

        if (!needsQuoting)
        {
            return value;
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    // A plain scalar may not begin with one of YAML's indicators: "[x] y" would be read as a flow
    // sequence, "&x" as an anchor and "*x" as an alias. A dash, a hash, a colon and the quotes are handled above.
    private static bool StartsWithIndicator(string value) => "[]{}&*!|>%@`?,".Contains(value[0], StringComparison.Ordinal);
}
