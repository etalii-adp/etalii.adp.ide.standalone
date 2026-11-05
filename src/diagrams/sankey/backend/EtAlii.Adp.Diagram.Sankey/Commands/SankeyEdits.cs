using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Turns an edit into the FBL model changes it is, which the library plans as splices of the body
/// (sankey-disl-fbl Requirement 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here builds a line of YAML.</b> Each method states the attributes it sets, the entry it adds, moves or
/// removes, in the binding's names, and <see cref="SankeyBody"/> hands that to FBL: where a key goes, how a value is
/// quoted, what indentation and line ending a new entry gets and what happens to the comments around an entry all
/// follow the binding and the body's own conventions (FBL section 6.3), not code in this module (Q1).
/// </para>
/// <para>
/// <b>An entry is found where the module read it</b>, by its rule and its first line, so a node without an id, a
/// later holder of an id and a flow, whose id is its place, are each the entry the model names.
/// </para>
/// <para>
/// <b>What stays here is what FBL does not decide</b>: the module's own refusals, and the order of the changes a
/// command makes (a flow before the node it runs from, a node added and then taken to its place in its column).
/// </para>
/// </remarks>
internal static class SankeyEdits
{
    /// <summary>The refusal for an entry that is not there, in one wording.</summary>
    public static EditOutcome Gone => EditOutcome.Refused("That is no longer in this diagram.");

    /// <summary>Writes a node's text key, removing it instead when <paramref name="removeWhenEmpty"/> and the value is blank.</summary>
    public static EditOutcome SetText(SankeyBody body, SankeyNode node, string key, string value, bool removeWhenEmpty) =>
        Set(body, body.EntryAt(SankeyReader.NodeRule, node.Range), key, Text(value, removeWhenEmpty));

    /// <summary>Writes a flow's text key, removing it instead when <paramref name="removeWhenEmpty"/> and the value is blank.</summary>
    public static EditOutcome SetText(SankeyBody body, SankeyFlow flow, string key, string value, bool removeWhenEmpty) =>
        Set(body, body.EntryAt(SankeyReader.FlowRule, flow.Range), key, Text(value, removeWhenEmpty));

    /// <summary>Writes a node's whole-number key, or removes it for <c>null</c>.</summary>
    public static EditOutcome SetNumber(SankeyBody body, SankeyNode node, string key, int? value) =>
        Set(body, body.EntryAt(SankeyReader.NodeRule, node.Range), key, value);

    /// <summary>Writes a flow's number key, or removes it for <c>null</c>.</summary>
    public static EditOutcome SetNumber(SankeyBody body, SankeyFlow flow, string key, double? value) =>
        Set(body, body.EntryAt(SankeyReader.FlowRule, flow.Range), key, value);

    /// <summary>
    /// Writes the document-wide <c>thickness</c>, where the document states it or where the binding orders it; refused
    /// for a body whose root is not a mapping, which has no key to write it beside.
    /// </summary>
    public static EditOutcome SetThickness(SankeyBody body, double thickness)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.Root is null
            ? EditOutcome.Refused("This file does not begin with a mapping such as `sankey: 1`, so there is nowhere to write `thickness`.")
            : Set(body, body.Root, "thickness", thickness);
    }

    /// <summary>
    /// Removes a node and every flow written to or from its id: the flows first, the last in the body first, so each
    /// change finds the entries above it where they were read.
    /// </summary>
    public static EditOutcome RemoveNode(SankeyBody body, SankeyNode node)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(node);

        var model = body.Reading.Model;
        foreach (var flow in model.Flows.Where(flow => flow.From == node.Id || flow.To == node.Id).OrderByDescending(flow => flow.Range.Start))
        {
            if (Remove(body, SankeyReader.FlowRule, flow.Range) is { WasApplied: false } refused)
            {
                return refused;
            }
        }

        // The flows above the node, when its list comes after theirs, moved it up: it is found again by its id.
        return body.Reading.Model.NodeOf(node.Id) is { } still ? Remove(body, SankeyReader.NodeRule, still.Range) : Gone;
    }

    /// <summary>Removes one flow.</summary>
    public static EditOutcome RemoveFlow(SankeyBody body, SankeyFlow flow)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(flow);

        return Remove(body, SankeyReader.FlowRule, flow.Range);
    }

    /// <summary>
    /// Moves a node's entry so it comes directly before <paramref name="anchor"/>, or directly after it when
    /// <paramref name="after"/> - which is how a node changes its place in its column, since a column is drawn top to
    /// bottom in the order its nodes are written. The entry keeps its own bytes.
    /// </summary>
    public static EditOutcome MoveNode(SankeyBody body, SankeyNode node, SankeyNode anchor, bool after)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(anchor);

        if (node.Id == anchor.Id)
        {
            return EditOutcome.Refused("A node cannot be moved next to itself.");
        }

        if (body.EntryAt(SankeyReader.NodeRule, node.Range) is not { } moved || body.EntryAt(SankeyReader.NodeRule, anchor.Range) is not { } beside)
        {
            return Gone;
        }

        return body.Change(new ModelChange.Move(moved.Id, moved.ParentId, body.IndexOf(beside) + (after ? 1 : 0)));
    }

    /// <summary>
    /// Adds a node with its id, its name, and its colour and column when it has them: after the last node, then
    /// directly before <paramref name="before"/> when one is given, so it lands at its place in its column. The
    /// binding adds an entry at the end of its list only (FBL section 6.1), so the place is a move.
    /// </summary>
    public static EditOutcome AddNode(SankeyBody body, SankeyNode node, SankeyNode? before = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(node);

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = node.Name };
        if (node.Color.Length > 0)
        {
            attributes["color"] = node.Color;
        }

        if (node.Column is { } column)
        {
            attributes["column"] = column;
        }

        var added = body.Change(new ModelChange.Add("Node", node.Id, attributes));
        if (!added.WasApplied || before is null)
        {
            return added;
        }

        var entries = body.Model.Elements.Where(element => element.Rule == SankeyReader.NodeRule).ToList();
        return entries.LastOrDefault(element => element.Id == node.Id) is { } entry && body.EntryAt(SankeyReader.NodeRule, before.Range) is { } beside
            ? body.Change(new ModelChange.Move(entry.Id, entry.ParentId, body.IndexOf(beside)))
            : added;
    }

    /// <summary>Adds a flow after the last one: its two ends and its value, and no id - its ends are its name.</summary>
    public static EditOutcome AddFlow(SankeyBody body, SankeyFlow flow)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(flow);

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["from"] = flow.From, ["to"] = flow.To };
        if (flow.Value is { } value)
        {
            attributes["value"] = value;
        }

        return body.Change(new ModelChange.Add("Flow", null, attributes));
    }

    private static string? Text(string value, bool removeWhenEmpty) => removeWhenEmpty && string.IsNullOrWhiteSpace(value) ? null : value;

    private static EditOutcome Set(SankeyBody body, FblElement? entry, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(body);
        return entry is null ? Gone : body.Change(new ModelChange.Set(entry.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = value }));
    }

    private static EditOutcome Remove(SankeyBody body, string rule, LineRange range) =>
        body.EntryAt(rule, range) is { } entry ? body.Change(new ModelChange.Remove(entry.Id)) : Gone;
}
