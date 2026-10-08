using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>What an edit did, or why it did nothing.</summary>
/// <param name="Refusal">The sentence to show, or <c>null</c> when the edit was spliced.</param>
public readonly record struct SankeyEdit(string? Refusal)
{
    /// <summary>The edit was spliced into the document.</summary>
    public static SankeyEdit Applied { get; } = new(null);

    /// <summary>The edit was refused, and this is why.</summary>
    public static SankeyEdit Refused(string because) => new(because);

    /// <summary>Whether the document changed.</summary>
    public bool WasApplied => Refusal is null;
}

/// <summary>
/// Turns an edit into a splice of the lines that edit affects, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing serialises a model back to a file.</b> Every write splices a range the parser
/// recorded, so a line nobody splices is a line nobody can damage - an unknown key, a comment and
/// the author's own indentation all survive by construction.
/// </para>
/// <para>
/// <b>A write invalidates the model that located it.</b> Line numbers move after a splice, so a
/// caller making several edits applies them bottom-up or re-parses between them.
/// </para>
/// </remarks>
public static class SankeyWriter
{
    internal const string NodesSection = SankeyParser.NodesKey + ":";
    internal const string FlowsSection = SankeyParser.FlowsKey + ":";

    /// <summary>Writes a text key, removing it instead when <paramref name="removeWhenEmpty"/> and the value is blank.</summary>
    public static SankeyEdit SetText(LineDocument document, LineRange range, string key, string value, bool removeWhenEmpty)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (removeWhenEmpty && string.IsNullOrWhiteSpace(value))
        {
            LineSplice.RemoveKey(document, range, key);
        }
        else
        {
            LineSplice.SetKey(document, range, key, Text(value ?? ""));
        }

        return SankeyEdit.Applied;
    }

    /// <summary>Writes a number key, or removes it for <c>null</c>.</summary>
    public static SankeyEdit SetNumber(LineDocument document, LineRange range, string key, double? value)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (value is null)
        {
            LineSplice.RemoveKey(document, range, key);
        }
        else
        {
            LineSplice.SetKey(document, range, key, Number(value.Value));
        }

        return SankeyEdit.Applied;
    }

    /// <summary>
    /// Writes one of the document-wide keys on its own line, or adds it under the header when the
    /// document does not state it yet.
    /// </summary>
    /// <param name="line">The key's zero-based line as the parser found it, or -1.</param>
    public static SankeyEdit SetRootKey(LineDocument document, int line, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (line >= 0 && line < document.Lines.Count)
        {
            document.Replace(new LineRange(line, line), [$"{key}: {value}"]);
            return SankeyEdit.Applied;
        }

        var header = LineSplice.FindSection(document, SankeyParser.HeaderKey + ":");
        document.Insert(header >= 0 ? header + 1 : 0, [$"{key}: {value}"]);
        return SankeyEdit.Applied;
    }

    /// <summary>Removes a node and every flow to or from it, bottom-up.</summary>
    public static SankeyEdit RemoveNode(LineDocument document, SankeyModel model, SankeyNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        RemoveBottomUp(document, model.Flows
            .Where(flow => flow.From == node.Id || flow.To == node.Id)
            .Select(flow => flow.Range)
            .Append(node.Range));
        return SankeyEdit.Applied;
    }

    /// <summary>Removes one entry - a flow, typically.</summary>
    public static SankeyEdit Remove(LineDocument document, LineRange range)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Remove(range);
        return SankeyEdit.Applied;
    }

    /// <summary>
    /// Moves a node's entry so it comes directly before <paramref name="anchor"/>, or directly after
    /// it when <paramref name="after"/> - which is how a node changes its place in its column, since
    /// a column is drawn top to bottom in the order its nodes are written.
    /// </summary>
    /// <remarks>
    /// The entry's own lines move, comments inside it included, and nothing else is touched: the
    /// lines around both places stay where they were.
    /// </remarks>
    public static SankeyEdit MoveNode(LineDocument document, SankeyNode node, SankeyNode anchor, bool after)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(anchor);

        if (node.Id == anchor.Id)
        {
            return SankeyEdit.Refused("A node cannot be moved next to itself.");
        }

        var lines = Enumerable.Range(node.Range.Start, node.Range.Length).Select(index => document.Lines[index].Text).ToList();
        var at = after ? anchor.Range.End + 1 : anchor.Range.Start;
        if (at > node.Range.Start)
        {
            at -= node.Range.Length;
        }

        document.Remove(node.Range);
        document.Insert(at, lines);
        return SankeyEdit.Applied;
    }

    /// <summary>
    /// Adds a node entry, matching whatever indentation the document already uses: directly before
    /// <paramref name="before"/> when one is given, so it lands at its place in its column, and after
    /// the last node otherwise.
    /// </summary>
    public static SankeyEdit AddNode(LineDocument document, SankeyModel model, SankeyNode node, SankeyNode? before = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        List<(string Key, string Value)> keys = [("name", Text(node.Name))];
        if (node.Color.Length > 0)
        {
            keys.Add(("color", Text(node.Color)));
        }

        if (node.Column is { } column)
        {
            keys.Add(("column", column.ToString(CultureInfo.InvariantCulture)));
        }

        var ranges = model.Nodes.Select(existing => existing.Range).ToList();
        if (before is null)
        {
            return Append(document, ranges, NodesSection, node.Id, keys);
        }

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, ranges);
        document.Insert(before.Range.Start, Entry(itemIndent, dashGap, keyIndent, node.Id, keys));
        return SankeyEdit.Applied;
    }

    /// <summary>Appends a flow entry: its two ends and its value, and no id - its ends are its name.</summary>
    public static SankeyEdit AddFlow(LineDocument document, SankeyModel model, SankeyFlow flow)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);

        var ranges = model.Flows.Select(existing => existing.Range).ToList();
        var at = LineSplice.InsertionPointFor(document, ranges, FlowsSection);
        if (at < 0)
        {
            // A hand-written document may leave the section out; the entry brings its section with it.
            document.Insert(document.Lines.Count, [FlowsSection]);
            at = document.Lines.Count;
        }

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, ranges);
        List<string> lines = [$"{itemIndent}-{dashGap}from: {Text(flow.From)}", $"{keyIndent}to: {Text(flow.To)}"];
        if (flow.Value is { } value)
        {
            lines.Add($"{keyIndent}value: {Number(value)}");
        }

        document.Insert(at, lines);
        return SankeyEdit.Applied;
    }

    private static SankeyEdit Append(
        LineDocument document,
        IReadOnlyList<LineRange> ranges,
        string section,
        string id,
        IReadOnlyList<(string Key, string Value)> keys)
    {
        var at = LineSplice.InsertionPointFor(document, ranges, section);
        if (at < 0)
        {
            // A hand-written document may leave the section out; the entry brings its section with it.
            document.Insert(document.Lines.Count, [section]);
            at = document.Lines.Count;
        }

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, ranges);
        document.Insert(at, Entry(itemIndent, dashGap, keyIndent, id, keys));
        return SankeyEdit.Applied;
    }

    private static List<string> Entry(string itemIndent, string dashGap, string keyIndent, string id, IReadOnlyList<(string Key, string Value)> keys)
    {
        List<string> lines = [$"{itemIndent}-{dashGap}id: {Text(id)}", .. keys.Select(entry => $"{keyIndent}{entry.Key}: {entry.Value}")];
        return lines;
    }

    private static void RemoveBottomUp(LineDocument document, IEnumerable<LineRange> ranges)
    {
        foreach (var range in ranges.OrderByDescending(range => range.Start))
        {
            document.Remove(range);
        }
    }

    /// <summary>
    /// A text value as YAML reads it back: plain where it can be, double-quoted where a plain
    /// scalar would mean something else.
    /// </summary>
    /// <remarks>
    /// The shared <see cref="LineSplice.Quote"/> covers colons, hashes and leading dashes; a value
    /// like <c>{value} TWh</c>, <c>[EU]</c>, <c>yes</c> or <c>12</c> would read back as a mapping,
    /// a list, a boolean or a number, so those are quoted here as well.
    /// </remarks>
    internal static string Text(string value)
    {
        var quoted = LineSplice.Quote(value);
        if (quoted.StartsWith('"'))
        {
            return quoted;
        }

        var reads = value.Length > 0 && ("[]{}&*!|>%@`,?'\"".Contains(value[0], StringComparison.Ordinal)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("null", StringComparison.OrdinalIgnoreCase)
            || value == "~");

        return reads ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : quoted;
    }

    /// <summary>A number the way the document reads it: invariant culture, no trailing zeros.</summary>
    internal static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
