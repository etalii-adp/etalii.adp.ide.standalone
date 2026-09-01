namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every write in this module goes through here, and every one of them is a splice of a range the
/// parser recorded. Nothing serialises a model back to a file, which is what makes Requirement
/// 2.2 - only the affected lines change - true by construction rather than by care, and
/// Requirement 2.3 - unmodelled keys survive - free, since a line nobody splices is a line nobody
/// can damage.
/// </para>
/// <para>
/// A write invalidates the model that located it: line numbers after a splice have moved. Callers
/// re-parse rather than adjusting ranges by hand, which is cheap and removes a whole class of
/// off-by-one bug.
/// </para>
/// </remarks>
public static class TimelineWriter
{
    private const string DefaultItemIndent = "  ";
    private const string DefaultKeyIndent = "    ";

    /// <summary>Rewrites an element's label, leaving its quoting style alone elsewhere.</summary>
    public static void SetLabel(TimelineDocument document, TimelineElement element, string label) =>
        SetKey(document, element.Range, "label", Quote(label));

    /// <summary>Rewrites an element's begin.</summary>
    public static void SetBegin(TimelineDocument document, TimelineElement element, string begin) =>
        SetKey(document, element.Range, "begin", begin);

    /// <summary>Rewrites an element's row.</summary>
    public static void SetRow(TimelineDocument document, TimelineElement element, int row) =>
        SetKey(document, element.Range, "row", row.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Rewrites an element's end, or removes the key entirely when <paramref name="end"/> is null -
    /// which is how a period becomes a moment (Requirement 11.3).
    /// </summary>
    public static void SetEnd(TimelineDocument document, TimelineElement element, string? end)
    {
        if (end is null)
        {
            RemoveKey(document, element.Range, "end");
            return;
        }

        SetKey(document, element.Range, "end", end);
    }

    /// <summary>Rewrites a connection's label, or removes the key when the label is cleared.</summary>
    public static void SetConnectionLabel(TimelineDocument document, TimelineConnection connection, string label)
    {
        if (label.Length == 0)
        {
            RemoveKey(document, connection.Range, "label");
            return;
        }

        SetKey(document, connection.Range, "label", Quote(label));
    }

    /// <summary>
    /// Appends an element to the document, after the last one already there.
    /// </summary>
    /// <remarks>
    /// The indentation is copied from whatever is already in the file rather than imposed, so a
    /// document written with four spaces stays written with four spaces (Requirement 2.2). Only an
    /// empty document falls back to this module's own default.
    /// </remarks>
    public static void InsertElement(
        TimelineDocument document,
        TimelineModel model,
        string id,
        string label,
        string begin,
        string? end,
        int row)
    {
        var (itemIndent, dashGap, keyIndent) = IndentOf(document, model.Elements.Select(element => element.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}label: {Quote(label)}",
            $"{keyIndent}begin: {begin}",
        };

        if (end is not null)
        {
            lines.Add($"{keyIndent}end: {end}");
        }

        lines.Add($"{keyIndent}row: {row.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        document.Insert(InsertionPointFor(document, model.Elements.Select(element => element.Range), "elements:"), lines);
    }

    /// <summary>Appends a connection to the document.</summary>
    public static void InsertConnection(
        TimelineDocument document,
        TimelineModel model,
        string id,
        string from,
        string to,
        string label)
    {
        var (itemIndent, dashGap, keyIndent) = IndentOf(document, model.Connections.Select(connection => connection.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}from: {from}",
            $"{keyIndent}to: {to}",
        };

        if (label.Length > 0)
        {
            lines.Add($"{keyIndent}label: {Quote(label)}");
        }

        var at = InsertionPointFor(document, model.Connections.Select(connection => connection.Range), "connections:");
        if (at < 0)
        {
            // No `connections:` key yet, so the section is created at the end of the document
            // along with its first entry.
            document.Insert(document.Lines.Count, ["connections:", .. lines]);
            return;
        }

        document.Insert(at, lines);
    }

    /// <summary>
    /// Removes an element and every connection that referenced it, in one splice each, from the
    /// bottom of the document upwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Descending order matters: removing a range shifts every line after it, so removing top-down
    /// would leave every later range pointing at the wrong lines. Sorting once here is cheaper
    /// than recomputing ranges between removals, and considerably harder to get wrong.
    /// </para>
    /// <para>
    /// The connections go with the element because a connection to something that no longer exists
    /// is not a diagram anybody wants; Requirement 2.5 asks that the count be known beforehand,
    /// which <see cref="ConnectionsTouching"/> answers without performing the removal.
    /// </para>
    /// </remarks>
    public static void RemoveElement(TimelineDocument document, TimelineModel model, TimelineElement element)
    {
        var ranges = ConnectionsTouching(model, element.Id)
            .Select(connection => connection.Range)
            .Append(element.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }
    }

    /// <summary>Removes one connection.</summary>
    public static void RemoveConnection(TimelineDocument document, TimelineConnection connection) =>
        document.Remove(connection.Range);

    /// <summary>Whether the document currently carries a <c>connections:</c> section key.</summary>
    /// <remarks>
    /// An insert creates the section on demand, so a command whose inverse must restore the file
    /// byte for byte asks this first and has its undo remove what the insert created - a stray
    /// <c>connections:</c> header after an undo was the one line that broke identity.
    /// </remarks>
    public static bool HasConnectionsSection(TimelineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return FindSection(document, "connections:") >= 0;
    }

    /// <summary>
    /// Removes a <c>connections:</c> section key left with no entries - the undo of the insert
    /// that created it. A no-op while any relation remains.
    /// </summary>
    public static void RemoveConnectionsSectionIfEmpty(TimelineDocument document, TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Connections.Count > 0)
        {
            return;
        }

        var index = FindSection(document, "connections:");
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    private static int FindSection(TimelineDocument document, string sectionKey)
    {
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
    /// The connections that would go with an element, so an action can say how many before it runs
    /// (Requirement 2.5).
    /// </summary>
    public static IReadOnlyList<TimelineConnection> ConnectionsTouching(TimelineModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Connections
            .Where(connection =>
                string.Equals(connection.From, elementId, StringComparison.Ordinal) ||
                string.Equals(connection.To, elementId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Replaces one key's value inside a range, keeping the line's own indentation, or adds the
    /// key when it is not there.
    /// </summary>
    private static void SetKey(TimelineDocument document, LineRange range, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = FindKey(document, range, key);
        if (index >= 0)
        {
            var existing = document.Lines[index].Text;
            var indent = existing[..(existing.Length - existing.TrimStart().Length)];
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

    private static void RemoveKey(TimelineDocument document, LineRange range, string key)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = FindKey(document, range, key);
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    private static int FindKey(TimelineDocument document, LineRange range, string key)
    {
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

    /// <summary>The indentation the keys inside a range already use.</summary>
    private static string KeyIndentWithin(TimelineDocument document, LineRange range)
    {
        for (var i = range.Start + 1; i <= range.End && i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text[..(text.Length - text.TrimStart().Length)];
            }
        }

        // A one-line element has no neighbour to copy, so the dash's own indentation plus two
        // spaces puts the new key under it - which is what the `- ` prefix occupies.
        var first = document.Lines[range.Start].Text;
        return first[..(first.Length - first.TrimStart().Length)] + DefaultItemIndent;
    }

    /// <summary>
    /// The item indentation, the gap after the dash, and the key indentation an existing
    /// declaration uses - or this module's defaults when the document has no example to copy.
    /// </summary>
    /// <remarks>
    /// The gap after the dash is copied along with the indent because a document written
    /// <c>-   id:</c> throughout and given one new entry written <c>- id:</c> is visibly ADP's
    /// work rather than the author's. Requirement 2.2 governs existing lines and would permit
    /// either, but matching what is already there costs three lines and is what the author would
    /// have written.
    /// </remarks>
    private static (string ItemIndent, string DashGap, string KeyIndent) IndentOf(
        TimelineDocument document, IEnumerable<LineRange> ranges)
    {
        var first = ranges.Cast<LineRange?>().FirstOrDefault();
        if (first is null)
        {
            return (DefaultItemIndent, " ", DefaultKeyIndent);
        }

        var range = first.Value;
        var dash = document.Lines[range.Start].Text;
        var itemIndent = dash[..(dash.Length - dash.TrimStart().Length)];

        var afterDash = dash.TrimStart();
        var gap = " ";
        if (afterDash.StartsWith('-'))
        {
            var rest = afterDash[1..];
            gap = rest[..(rest.Length - rest.TrimStart().Length)];
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
    /// A section written flow-empty - <c>elements: []</c>, which is what a fresh document from
    /// the factory says, and what a hand author may equally write - is first opened into a bare
    /// <c>elements:</c> key, because appending a block entry after a line that already carries a
    /// value would leave the key with two values and the document unparseable.
    /// </remarks>
    private static int InsertionPointFor(TimelineDocument document, IEnumerable<LineRange> ranges, string sectionKey)
    {
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
                var indent = text[..(text.Length - text.TrimStart().Length)];
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
    private static string Quote(string value)
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
            value.StartsWith('\'');

        return needsQuoting
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
