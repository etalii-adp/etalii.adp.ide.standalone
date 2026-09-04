using System.Globalization;
using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every write in this module goes through here, and every one of them is a splice of a range the
/// parser recorded. Nothing serialises a model back to a file, which is what makes "only the
/// affected lines change" true by construction rather than by care, and "unmodelled keys survive"
/// free, since a line nobody splices is a line nobody can damage.
/// </para>
/// <para>
/// A write invalidates the model that located it: line numbers after a splice have moved. Callers
/// re-parse rather than adjusting ranges by hand, which is cheap and removes a whole class of
/// off-by-one bug.
/// </para>
/// </remarks>
public static class DependencyGraphWriter
{
    /// <summary>The document's section key for the directed depends-on edges.</summary>
    internal const string RelationsSection = "relations:";

    private const string ElementsSection = "elements:";
    private const string DefaultItemIndent = "  ";
    private const string DefaultKeyIndent = "    ";

    /// <summary>Rewrites an element's label, leaving its quoting style alone elsewhere.</summary>
    public static void SetLabel(DependencyGraphDocument document, DependencyGraphElement element, string label)
    {
        ArgumentNullException.ThrowIfNull(element);
        SetKey(document, element.Range, "label", Quote(label));
    }

    /// <summary>Rewrites an element's horizontal coordinate.</summary>
    public static void SetX(DependencyGraphDocument document, DependencyGraphElement element, double x)
    {
        ArgumentNullException.ThrowIfNull(element);
        SetKey(document, element.Range, "x", Number(x));
    }

    /// <summary>Rewrites an element's row.</summary>
    public static void SetRow(DependencyGraphDocument document, DependencyGraphElement element, int row)
    {
        ArgumentNullException.ThrowIfNull(element);
        SetKey(document, element.Range, "row", row.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Rewrites a relation's label, or removes the key when the label is cleared.</summary>
    public static void SetRelationLabel(DependencyGraphDocument document, DependencyGraphRelation relation, string label)
    {
        ArgumentNullException.ThrowIfNull(relation);

        if (label.Length == 0)
        {
            RemoveKey(document, relation.Range, "label");
            return;
        }

        SetKey(document, relation.Range, "label", Quote(label));
    }

    /// <summary>
    /// Appends an element to the document, after the last one already there.
    /// </summary>
    /// <remarks>
    /// The indentation is copied from whatever is already in the file rather than imposed, so a
    /// document written with four spaces stays written with four spaces. Only an empty document
    /// falls back to this module's own default.
    /// </remarks>
    public static void InsertElement(
        DependencyGraphDocument document,
        DependencyGraphModel model,
        string id,
        string label,
        double x,
        int row)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var (itemIndent, dashGap, keyIndent) = IndentOf(document, model.Elements.Select(element => element.Range));
        var lines = new List<string>
        {
            $"{itemIndent}-{dashGap}id: {id}",
            $"{keyIndent}label: {Quote(label)}",
            $"{keyIndent}x: {Number(x)}",
            $"{keyIndent}row: {row.ToString(CultureInfo.InvariantCulture)}",
        };

        document.Insert(
            InsertionPointFor(document, model.Elements.Select(element => element.Range), ElementsSection),
            lines);
    }

    /// <summary>
    /// Appends a relation to the document: <paramref name="from"/> depends on <paramref name="to"/>.
    /// </summary>
    public static void InsertRelation(
        DependencyGraphDocument document,
        DependencyGraphModel model,
        string id,
        string from,
        string to,
        string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var (itemIndent, dashGap, keyIndent) = IndentOf(document, model.Relations.Select(relation => relation.Range));
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

        var at = InsertionPointFor(document, model.Relations.Select(relation => relation.Range), RelationsSection);
        if (at < 0)
        {
            // No `relations:` key yet, so the section is created at the end of the document
            // along with its first entry.
            document.Insert(document.Lines.Count, [RelationsSection, .. lines]);
            return;
        }

        document.Insert(at, lines);
    }

    /// <summary>
    /// Removes an element and every relation that referenced it, in one splice each, from the
    /// bottom of the document upwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Descending order matters: removing a range shifts every line after it, so removing top-down
    /// would leave every later range pointing at the wrong lines. Sorting once here is cheaper
    /// than recomputing ranges between removals, and considerably harder to get wrong.
    /// </para>
    /// <para>
    /// The relations go with the element because a dependency on something that no longer exists
    /// is not a graph anybody wants; the count is wanted beforehand, which
    /// <see cref="RelationsTouching"/> answers without performing the removal.
    /// </para>
    /// </remarks>
    public static void RemoveElement(
        DependencyGraphDocument document, DependencyGraphModel model, DependencyGraphElement element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        var ranges = RelationsTouching(model, element.Id)
            .Select(relation => relation.Range)
            .Append(element.Range)
            .OrderByDescending(range => range.Start)
            .ToList();

        foreach (var range in ranges)
        {
            document.Remove(range);
        }
    }

    /// <summary>Removes one relation.</summary>
    public static void RemoveRelation(DependencyGraphDocument document, DependencyGraphRelation relation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relation);
        document.Remove(relation.Range);
    }

    /// <summary>Whether the document currently carries a <c>relations:</c> section key.</summary>
    /// <remarks>
    /// An insert creates the section on demand, so a command whose inverse must restore the file
    /// byte for byte asks this first and has its undo remove what the insert created - a stray
    /// <c>relations:</c> header after an undo is the one line that would break identity.
    /// </remarks>
    public static bool HasRelationsSection(DependencyGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return FindSection(document, RelationsSection) >= 0;
    }

    /// <summary>
    /// Removes a <c>relations:</c> section key left with no entries - the undo of the insert
    /// that created it. A no-op while any relation remains.
    /// </summary>
    public static void RemoveRelationsSectionIfEmpty(DependencyGraphDocument document, DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Relations.Count > 0)
        {
            return;
        }

        var index = FindSection(document, RelationsSection);
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    /// <summary>
    /// The relations that would go with an element, so an action can say how many before it runs.
    /// </summary>
    public static IReadOnlyList<DependencyGraphRelation> RelationsTouching(DependencyGraphModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.Relations
            .Where(relation =>
                string.Equals(relation.From, elementId, StringComparison.Ordinal) ||
                string.Equals(relation.To, elementId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// A coordinate as document text: invariant, and in its shortest round-trippable form so a
    /// whole number stays a whole number rather than sprouting a decimal point on every drag.
    /// </summary>
    internal static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static int FindSection(DependencyGraphDocument document, string sectionKey)
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
    /// Replaces one key's value inside a range, keeping the line's own indentation, or adds the
    /// key when it is not there.
    /// </summary>
    private static void SetKey(DependencyGraphDocument document, LineRange range, string key, string value)
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

    private static void RemoveKey(DependencyGraphDocument document, LineRange range, string key)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = FindKey(document, range, key);
        if (index >= 0)
        {
            document.Remove(new LineRange(index, index));
        }
    }

    private static int FindKey(DependencyGraphDocument document, LineRange range, string key)
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
    private static string KeyIndentWithin(DependencyGraphDocument document, LineRange range)
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
    /// work rather than the author's. Matching what is already there costs three lines and is
    /// what the author would have written.
    /// </remarks>
    private static (string ItemIndent, string DashGap, string KeyIndent) IndentOf(
        DependencyGraphDocument document, IEnumerable<LineRange> ranges)
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
    private static int InsertionPointFor(
        DependencyGraphDocument document, IEnumerable<LineRange> ranges, string sectionKey)
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
