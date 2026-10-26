using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Writes what a transaction of the definition says (DISL §9.4) through today's <see cref="SupplyChainWriter"/>, so
/// the bytes an edit writes do not change while the edit is decided by the definition. It stands in for FBL's splices
/// until every command writes through <see cref="SupplyChainBody"/> (supply-chain-disl-fbl, task 11), which removes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>An element is the entry it was read from</b>: its type and its line in the model the transaction ran over, so a
/// later holder of an id and an entry without one are written where they are.
/// </para>
/// <para>
/// <b>The entries are edited bottom-up, then the new ones appended</b>, each new one into the document as it now is
/// read.
/// Within an entry the changes are written in the transaction's order, and an <c>x</c> and a <c>y</c> as the writer
/// places a node: <c>y</c> first, so a newly placed entry reads <c>x</c> before <c>y</c>. Editing bottom-up leaves
/// every range above valid; it is how the writer has always removed a node with its flows and a group from its
/// members.
/// </para>
/// <para>
/// <b>A change it cannot write refuses the edit</b> with a sentence naming it, never dropped silently.
/// </para>
/// </remarks>
internal static class SupplyChainApply
{
    /// <summary>
    /// Writes <paramref name="transaction"/>, run over the DISL model of <paramref name="model"/> (<paramref name="before"/>
    /// as it was before the transaction ran), into <paramref name="document"/>; or its refusal.
    /// </summary>
    public static EditOutcome Apply(LineDocument document, SupplyChainModel model, DislDiagram before, DislTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied)
        {
            return EditOutcome.Refused(transaction.Refusal!);
        }

        var created = transaction.Changes
            .Where(change => change is DislChange.Create or DislChange.Connect)
            .ToList();
        var edits = new List<(Entry Entry, DislChange Change)>();
        foreach (var change in transaction.Changes.Except(created))
        {
            if (EntryOf(model, before, change) is not { } entry)
            {
                return Unwritable(change);
            }

            edits.Add((entry, change));
        }

        foreach (var entry in edits.GroupBy(edit => edit.Entry).OrderByDescending(entry => entry.Key.Range.Start))
        {
            var range = entry.Key.Range;
            foreach ((var _, DislChange change) in entry)
            {
                range = change is DislChange.Set set
                    ? Set(document, before, entry.Key.Kind, range, set)
                    : Removed(document, range);
            }
        }

        foreach (var change in created)
        {
            var outcome = Create(document, before, change);
            if (!outcome.WasApplied)
            {
                return outcome;
            }
        }

        return EditOutcome.Applied;
    }

    /// <summary>The refusal of a change no write of this module makes.</summary>
    private static EditOutcome Unwritable(DislChange change) =>
        EditOutcome.Refused($"The definition makes a change this diagram cannot write: {change.GetType().Name.ToLowerInvariant()}.");

    /// <summary>The entry <paramref name="change"/> changes, located in <paramref name="model"/>; null for a change this module does not write.</summary>
    private static Entry? EntryOf(SupplyChainModel model, DislDiagram before, DislChange change) => change switch
    {
        DislChange.Remove remove => EntryOf(model, before, remove.ElementId),
        DislChange.Set set when EntryOf(model, before, set.ElementId) is { } entry && Writes(set, entry.Kind) => entry,
        _ => null,
    };

    /// <summary>The entry <paramref name="elementId"/> was read from: its kind and its range in <paramref name="model"/>.</summary>
    private static Entry? EntryOf(SupplyChainModel model, DislDiagram before, string elementId)
    {
        if (before.ElementById(elementId) is not { Line: { } line } element)
        {
            return null;
        }

        var start = line - 1;
        LineRange? range = element.Type.Name switch
        {
            "Group" => model.Groups.FirstOrDefault(group => group.Range.Start == start)?.Range,
            "Stage" => model.Nodes.FirstOrDefault(node => node.Range.Start == start)?.Range,
            "Flow" => model.Flows.FirstOrDefault(flow => flow.Range.Start == start)?.Range,
            _ => null,
        };
        return range is { } found ? new Entry(element.Type.Name, found) : null;
    }

    /// <summary>
    /// What a write into <paramref name="range"/> leaves of it: its start, and its end moved by the lines the write
    /// added or took. Entries are edited bottom-up, so every line the document gained or lost is this entry's, and the
    /// document need not be read again, which a document the writer leaves unreadable could not be.
    /// </summary>
    private static LineRange Tracked(LineDocument document, LineRange range, Action<LineRange> write)
    {
        var lines = document.Lines.Count;
        write(range);
        return new LineRange(range.Start, range.End + (document.Lines.Count - lines));
    }

    private static LineRange Removed(LineDocument document, LineRange range)
    {
        document.Remove(range);
        return new LineRange(range.Start, range.Start);
    }

    /// <summary>Whether this module writes every attribute <paramref name="set"/> assigns on an entry of <paramref name="kind"/>.</summary>
    private static bool Writes(DislChange.Set set, string kind) =>
        set.Attributes.Keys.All(attribute => KeyOf(kind, attribute) is not null);

    /// <summary>The key an attribute of an entry of <paramref name="kind"/> is written as; null for one this module does not write.</summary>
    private static string? KeyOf(string kind, string attribute) => (kind, attribute) switch
    {
        ("Stage", "stage") => SupplyChainKeys.Type,
        ("Stage", "name" or "description" or "group" or "quantity" or "unit" or "step" or "x" or "y") => attribute,
        ("Flow", "product" or "description" or "volume" or "unit" or "step") => attribute,
        ("Group", "name" or "description" or "x" or "y") => attribute,
        _ => null,
    };

    private static LineRange Set(LineDocument document, DislDiagram before, string kind, LineRange range, DislChange.Set set)
    {
        var attributes = set.Attributes;
        foreach ((string attribute, object? value) in attributes)
        {
            if (attribute is "x" && attributes.ContainsKey("y"))
            {
                continue;
            }

            if (attribute is "y" && attributes.TryGetValue("x", out var x))
            {
                // y first: inserting a missing key lands it after the entry's first line, so writing y and then x
                // reads `x` before `y` in a newly placed entry, as SupplyChainWriter.Place writes them.
                range = Tracked(document, range, at => Place(document, at, "y", value));
                range = Tracked(document, range, at => Place(document, at, "x", x));
                continue;
            }

            var key = KeyOf(kind, attribute)!;
            range = Tracked(document, range, at => Write(document, before, at, key, value));
        }

        return range;
    }

    private static void Place(LineDocument document, LineRange range, string key, object? value)
    {
        if (Number(value) is { } number)
        {
            LineSplice.SetKey(document, range, key, DslNumber.Format(Math.Round(number)));
        }
        else
        {
            LineSplice.RemoveKey(document, range, key);
        }
    }

    private static void Write(LineDocument document, DislDiagram before, LineRange range, string key, object? value)
    {
        switch (key)
        {
            case SupplyChainKeys.Quantity or SupplyChainKeys.Volume or SupplyChainKeys.Step:
                SupplyChainWriter.SetNumber(document, range, key, Number(value));
                break;

            case SupplyChainKeys.Group when value is null:
                LineSplice.RemoveKey(document, range, key);
                break;

            case SupplyChainKeys.Group:
                SupplyChainWriter.SetText(document, range, key, WrittenIdOf(before, value), removeWhenEmpty: false);
                break;

            case SupplyChainKeys.Name or SupplyChainKeys.Type:
                SupplyChainWriter.SetText(document, range, key, value as string ?? "", removeWhenEmpty: false);
                break;

            default:
                SupplyChainWriter.SetText(document, range, key, value as string ?? "", removeWhenEmpty: true);
                break;
        }
    }

    private static EditOutcome Create(LineDocument document, DislDiagram before, DislChange change)
    {
        var model = SupplyChainReading.Parse(document.Text);
        switch (change)
        {
            case DislChange.Create { Type: "Stage" } create:
                {
                    var attributes = create.Attributes;
                    var node = new SupplyChainNode(
                        create.Id,
                        Text(attributes, "stage"),
                        Text(attributes, "name"),
                        Description: Text(attributes, "description"),
                        Group: attributes.TryGetValue("group", out var group) && group is not null ? WrittenIdOf(before, group) : "",
                        Quantity: Number(attributes.GetValueOrDefault("quantity")),
                        Unit: Text(attributes, "unit"),
                        Step: Number(attributes.GetValueOrDefault("step")),
                        X: Number(attributes.GetValueOrDefault("x")),
                        Y: Number(attributes.GetValueOrDefault("y")),
                        Range: new LineRange(0, 0));
                    return SupplyChainWriter.AddNode(document, model, node);
                }

            case DislChange.Create { Type: "Group" } create:
                {
                    var attributes = create.Attributes;
                    var group = new SupplyChainGroup(
                        create.Id,
                        Text(attributes, "name"),
                        Text(attributes, "description"),
                        new LineRange(0, 0),
                        Number(attributes.GetValueOrDefault("x")),
                        Number(attributes.GetValueOrDefault("y")));
                    return SupplyChainWriter.AddGroup(document, model, group);
                }

            case DislChange.Connect { Type: "Flow" } connect:
                {
                    var attributes = connect.Attributes;
                    var flow = new SupplyChainFlow(
                        connect.Id,
                        WrittenIdOf(before, connect.SourceId),
                        WrittenIdOf(before, connect.TargetId),
                        Product: Text(attributes, "product"),
                        Description: Text(attributes, "description"),
                        Volume: Number(attributes.GetValueOrDefault("volume")),
                        Unit: Text(attributes, "unit"),
                        Step: Number(attributes.GetValueOrDefault("step")),
                        new LineRange(0, 0));
                    return SupplyChainWriter.AddFlow(document, model, flow);
                }

            default:
                return Unwritable(change);
        }
    }

    /// <summary>
    /// The id <paramref name="value"/> is written with: an element of the model the transaction ran over by the id it is
    /// written with, and an element the transaction made by its own.
    /// </summary>
    private static string WrittenIdOf(DislDiagram before, object? value) => value switch
    {
        DislElement element when before.ElementById(element.Id) is { } read => SupplyChainWritten.IdOf(read),
        DislElement element => element.Id,
        string id when before.ElementById(id) is { } read => SupplyChainWritten.IdOf(read),
        string id => id,
        _ => "",
    };

    private static string Text(IReadOnlyDictionary<string, object?> attributes, string name) =>
        attributes.TryGetValue(name, out var value) && value is string text ? text : "";

    private static double? Number(object? value) => value switch
    {
        double number => number,
        long number => number,
        int number => number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    /// <summary>An entry a transaction changes: its kind, the definition's type name, and its range before any change.</summary>
    private sealed record Entry(string Kind, LineRange Range);

}
