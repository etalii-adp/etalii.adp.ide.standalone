using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>What an edit did, or why it did nothing.</summary>
/// <param name="Refusal">The sentence to show, or <c>null</c> when the edit was spliced.</param>
public readonly record struct SupplyChainEdit(string? Refusal)
{
    /// <summary>The edit was spliced into the document.</summary>
    public static SupplyChainEdit Applied { get; } = new(null);

    /// <summary>The edit was refused, and this is why.</summary>
    public static SupplyChainEdit Refused(string because) => new(because);

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
public static class SupplyChainWriter
{
    internal const string GroupsSection = SupplyChainParser.GroupsKey + ":";
    internal const string NodesSection = SupplyChainParser.NodesKey + ":";
    internal const string FlowsSection = SupplyChainParser.FlowsKey + ":";

    /// <summary>Writes a text key, removing it instead when <paramref name="removeWhenEmpty"/> and the value is blank.</summary>
    public static SupplyChainEdit SetText(LineDocument document, LineRange range, string key, string value, bool removeWhenEmpty)
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

        return SupplyChainEdit.Applied;
    }

    /// <summary>Writes a number key, or removes it for <c>null</c>.</summary>
    public static SupplyChainEdit SetNumber(LineDocument document, LineRange range, string key, double? value)
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

        return SupplyChainEdit.Applied;
    }

    /// <summary>
    /// Places several nodes at once, bottom-up so each splice leaves the ranges above it valid.
    /// </summary>
    /// <remarks>
    /// <c>x</c> is written before <c>y</c> into the same range: a key that is missing is inserted
    /// directly after the entry's first line, which moves nothing above the range.
    /// </remarks>
    public static SupplyChainEdit Place(LineDocument document, SupplyChainModel model, IReadOnlyDictionary<string, (double X, double Y)> places)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(places);

        var targets = model.Nodes
            .Where(node => places.ContainsKey(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(node => node.Range.Start)
            .ToList();

        if (targets.Count == 0)
        {
            return SupplyChainEdit.Refused("There is nothing here to place.");
        }

        foreach (var node in targets)
        {
            (double x, double y) = places[node.Id];
            // y first: inserting a missing key lands it after the entry's first line, so writing y
            // and then x reads `x` before `y` in a newly placed entry, as a person writes them.
            var grows = LineSplice.FindKey(document, node.Range, "y") < 0 ? 1 : 0;
            LineSplice.SetKey(document, node.Range, "y", Number(Math.Round(y)));
            LineSplice.SetKey(document, ExtendedBy(node.Range, grows), "x", Number(Math.Round(x)));
        }

        return SupplyChainEdit.Applied;
    }

    /// <summary>Places a group's own frame - what it is drawn at while it has no members.</summary>
    public static SupplyChainEdit PlaceGroup(LineDocument document, SupplyChainGroup group, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(group);

        // y first, for the same reason as Place: a newly placed entry reads `x` before `y`.
        var grows = LineSplice.FindKey(document, group.Range, "y") < 0 ? 1 : 0;
        LineSplice.SetKey(document, group.Range, "y", Number(Math.Round(y)));
        LineSplice.SetKey(document, ExtendedBy(group.Range, grows), "x", Number(Math.Round(x)));
        return SupplyChainEdit.Applied;
    }

    /// <summary>Removes a node and every flow to or from it, bottom-up.</summary>
    public static SupplyChainEdit RemoveNode(LineDocument document, SupplyChainModel model, SupplyChainNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        RemoveBottomUp(document, model.Flows
            .Where(flow => flow.From == node.Id || flow.To == node.Id)
            .Select(flow => flow.Range)
            .Append(node.Range));
        return SupplyChainEdit.Applied;
    }

    /// <summary>Removes a group, and its name from every node that was inside it.</summary>
    public static SupplyChainEdit RemoveGroup(LineDocument document, SupplyChainModel model, SupplyChainGroup group)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(group);

        // Members first and bottom-up, then the group itself: groups precede nodes in a document
        // this module writes, but a hand-written one may put them anywhere, so the order is by line.
        List<(int Start, Action Edit)> edits =
        [
            .. model.Nodes
                .Where(node => node.Group == group.Id)
                .Select(node => (node.Range.Start, (Action)(() => LineSplice.RemoveKey(document, node.Range, "group")))),
            (group.Range.Start, () => document.Remove(group.Range)),
        ];

        foreach ((var _, Action edit) in edits.OrderByDescending(edit => edit.Start))
        {
            edit();
        }

        return SupplyChainEdit.Applied;
    }

    /// <summary>Removes one entry - a flow, typically.</summary>
    public static SupplyChainEdit Remove(LineDocument document, LineRange range)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Remove(range);
        return SupplyChainEdit.Applied;
    }

    /// <summary>Appends a node entry, matching whatever indentation the document already uses.</summary>
    public static SupplyChainEdit AddNode(LineDocument document, SupplyChainModel model, SupplyChainNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        List<(string Key, string Value)> keys =
        [
            ("type", node.Type),
            ("name", Text(node.Name)),
        ];

        if (node.Group.Length > 0)
        {
            keys.Add(("group", Text(node.Group)));
        }

        if (node.Quantity is { } quantity)
        {
            keys.Add(("quantity", Number(quantity)));
        }

        if (node.Unit.Length > 0)
        {
            keys.Add(("unit", Text(node.Unit)));
        }

        if (node is { X: { } x, Y: { } y })
        {
            keys.Add(("x", Number(Math.Round(x))));
            keys.Add(("y", Number(Math.Round(y))));
        }

        return Append(document, model.Nodes.Select(existing => existing.Range), NodesSection, node.Id, keys);
    }

    /// <summary>Appends a group entry.</summary>
    public static SupplyChainEdit AddGroup(LineDocument document, SupplyChainModel model, SupplyChainGroup group)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(group);

        List<(string Key, string Value)> keys = [("name", Text(group.Name))];
        if (group is { X: { } x, Y: { } y })
        {
            keys.Add(("x", Number(Math.Round(x))));
            keys.Add(("y", Number(Math.Round(y))));
        }

        return Append(document, model.Groups.Select(existing => existing.Range), GroupsSection, group.Id, keys);
    }

    /// <summary>Appends a flow entry.</summary>
    public static SupplyChainEdit AddFlow(LineDocument document, SupplyChainModel model, SupplyChainFlow flow)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(flow);

        List<(string Key, string Value)> keys =
        [
            ("from", Text(flow.From)),
            ("to", Text(flow.To)),
        ];

        if (flow.Product.Length > 0)
        {
            keys.Add(("product", Text(flow.Product)));
        }

        if (flow.Volume is { } volume)
        {
            keys.Add(("volume", Number(volume)));
        }

        if (flow.Unit.Length > 0)
        {
            keys.Add(("unit", Text(flow.Unit)));
        }

        return Append(document, model.Flows.Select(existing => existing.Range), FlowsSection, flow.Id, keys);
    }

    private static SupplyChainEdit Append(
        LineDocument document,
        IEnumerable<LineRange> existing,
        string section,
        string id,
        IReadOnlyList<(string Key, string Value)> keys)
    {
        var ranges = existing.ToList();
        var at = LineSplice.InsertionPointFor(document, ranges, section);
        if (at < 0)
        {
            // A hand-written document may leave a section out; the entry brings its section with it.
            document.Insert(document.Lines.Count, [section]);
            at = document.Lines.Count;
        }

        (string itemIndent, string dashGap, string keyIndent) = LineSplice.IndentOf(document, ranges);
        List<string> lines = [$"{itemIndent}-{dashGap}id: {Text(id)}", .. keys.Select(entry => $"{keyIndent}{entry.Key}: {entry.Value}")];

        document.Insert(at, lines);
        return SupplyChainEdit.Applied;
    }

    private static void RemoveBottomUp(LineDocument document, IEnumerable<LineRange> ranges)
    {
        foreach (var range in ranges.OrderByDescending(range => range.Start))
        {
            document.Remove(range);
        }
    }

    private static LineRange ExtendedBy(LineRange range, int lines) => new(range.Start, range.End + lines);

    /// <summary>
    /// A text value as YAML reads it back: plain where it can be, double-quoted where a plain
    /// scalar would mean something else.
    /// </summary>
    /// <remarks>
    /// The shared <see cref="LineSplice.Quote"/> covers colons, hashes and leading dashes; a name
    /// like <c>[EU]</c>, <c>yes</c> or <c>12</c> would read back as a list, a boolean or a number,
    /// so those are quoted here as well.
    /// </remarks>
    internal static string Text(string value)
    {
        var quoted = LineSplice.Quote(value);
        if (quoted.StartsWith('"'))
        {
            return quoted;
        }

        var reads = value.Length > 0 && ("[]{}&*!|>%@`,?".Contains(value[0], StringComparison.Ordinal)
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
