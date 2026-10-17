using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The hype cycle graph's bundled DISL definition (<c>definition/gartner-hype-cycle-graph.dis</c>, from
/// etalii-adp/etalii.adp), loaded once as a <see cref="DislDiagramDefinition"/>, and what the providers
/// derive from it: the palette, the context menus and the property rows, mapped onto the host's types
/// with the wire ids of its <c>x-ghg</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the document's own</b>: <see cref="GhgBody.Disl"/>, built from the binding's
/// reading and kept with the bytes it was built from, so a menu and a row read the same snapshot and
/// no call builds it twice.
/// </para>
/// <para>
/// <b>An element is found by the id written in the document</b>, as <see cref="GhgModel"/> finds it:
/// the first trend with it, else the first trigger, else the first note, else the first influence. A
/// later entry reusing an id, or one without an id, is told apart by FBL by its place; the client
/// knows it by what is written, the binding's <c>storedId</c>.
/// </para>
/// <para>
/// <b>The menus are derived in true time</b> (<c>env.viewpoint</c>): the backend has no viewpoint, and the
/// canvas's own menu is compact's concern. <b>Read-only</b> (<c>env.readOnly</c>) is an entry that could not be read.
/// </para>
/// </remarks>
internal static class GhgDefinition
{
    /// <summary>The viewpoint the backend derives in.</summary>
    public const string Viewpoint = "trueTime";

    /// <summary>
    /// The definition, with its <c>x-ghg</c> wire ids. Arrange carries its sentence even while it is
    /// available, as the hand-written menu did.
    /// </summary>
    public static DislDiagramDefinition Definition { get; } = new(typeof(GhgDefinition).Assembly, "gartner-hype-cycle-graph.dis", "x-ghg", standingOperations: ["arrange"]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The wire ids of <c>x-ghg</c>.</summary>
    private static WireIdMap Ids => Definition.Ids;

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<GhgBody, GhgModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = entry.Document.Disl.Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GhgGestures.TryParseRelation(elementId, out var from, out _, out var to, out _)
                    ? DislMenuTarget.Connection(diagram, ElementOf(diagram, from), ElementOf(diagram, to), "Influence")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env(entry));
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: an element's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<GhgBody, GhgModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(entry.Document.Disl.Diagram, elementId) is not { } element
            ? []
            : WithUnresolvedEnds(element, Definition.Rows(element, Env(entry)));
    }

    /// <summary>The element <paramref name="id"/> names, as <see cref="GhgModel"/> finds it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        foreach (var type in (string[])["Trend", "Trigger", "Note"])
        {
            if (diagram.NodesOfType(type).FirstOrDefault(candidate => WrittenId(candidate) == id) is { } node) return node;
        }

        return diagram.RelationsOfType("Influence").FirstOrDefault(relation => WrittenId(relation) == id);
    }

    /// <summary>
    /// A point on the canvas as the definition's <c>position</c> reads it (DISL §12.3), in domain
    /// values: <c>x</c> the month index on the time axis, four canvas units per step of
    /// <paramref name="unit"/> from 1900-01, and <c>y</c> the row, 56 units apart.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Position(double x, double y, GhgTimeUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["x"] = GhgScale.OriginMonth + (x / GhgScale.UnitsPerStep * unit.Months),
            ["y"] = y / GhgScale.RowStep,
        };
    }

    /// <summary>Writes the changes of <paramref name="transaction"/> into <paramref name="document"/>, in order; the first refusal, the transaction's own or the binding's, stops it.</summary>
    public static EditOutcome Apply(GhgBody document, DislTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied) return EditOutcome.Refused(transaction.Refusal!);

        foreach (var change in transaction.Changes)
        {
            var edit = document.Change(DislWrite.ToFbl(Specification, change));
            if (!edit.WasApplied) return edit;
        }
        return EditOutcome.Applied;
    }

    /// <summary>What <c>env</c> reads for an edit: true time, editable.</summary>
    public static DislEnv EditEnv { get; } = new(Viewpoint: Viewpoint);

    /// <summary>The id written in the document, the binding's <c>storedId</c>: empty for an entry without one.</summary>
    public static string WrittenId(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.HostAttributes.TryGetValue("storedId", out var stored) ? GhgParser.Text(stored) ?? "" : "";
    }

    private static DislEnv Env(DocumentEntry<GhgBody, GhgModel> entry) => new(ReadOnly: !entry.IsUsable, Viewpoint: Viewpoint);

    /// <summary>
    /// The rows that list an influence end the document names but does not hold, with that end written
    /// as the code always wrote it: its id and phase, "x · Peak". DISL hands CEL a dangling end as null
    /// and gives it no way to read the id it was written with (§4.9), so the definition's <c>endText</c>
    /// fails there; every other row is the definition's.
    /// </summary>
    private static IReadOnlyList<ContextPropertyDefinition> WithUnresolvedEnds(DislElement element, IReadOnlyList<ContextPropertyDefinition> rows)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.IsA("Influence"))
        {
            if (element.Source is null || element.Target is null)
            {
                values[Ids.PropertyId("From")] = EndText(element.Source, element.SourceId, Text(element, "fromPhase"));
                values[Ids.PropertyId("To")] = EndText(element.Target, element.TargetId, Text(element, "toPhase"));
            }
        }
        else
        {
            foreach (var phase in (string[])["peak", "trough", "slope", "plateau"])
            {
                var leaving = element.Outgoing.Where(relation => relation.IsA("Influence") && Text(relation, "fromPhase") == phase).ToList();
                var arriving = element.Incoming.Where(relation => relation.IsA("Influence") && Text(relation, "toPhase") == phase).ToList();
                if (leaving.Any(relation => relation.Target is null))
                {
                    values[Ids.PropertyId($"{phase}Influences")] = ListOrNone(leaving.Select(relation => EndText(relation.Target, relation.TargetId, Text(relation, "toPhase"))));
                }

                if (arriving.Any(relation => relation.Source is null))
                {
                    values[Ids.PropertyId($"{phase}InfluencedBy")] = ListOrNone(arriving.Select(relation => EndText(relation.Source, relation.SourceId, Text(relation, "fromPhase"))));
                }
            }
        }

        return values.Count == 0 ? rows : [.. rows.Select(row => values.TryGetValue(row.Id, out var value) ? row with { Value = value } : row)];
    }

    /// <summary>The definition's <c>endText</c>, which also writes an end it cannot resolve: by the id it names.</summary>
    private static string EndText(DislElement? end, string? writtenId, string phase)
    {
        var name = end is null ? writtenId ?? "" : Text(end, "name") is { Length: > 0 } text ? text : end.Id;
        if (end is not null && end.IsA("Trigger")) return name;
        var title = phase is "peak" or "trough" or "slope" or "plateau" ? char.ToUpperInvariant(phase[0]) + phase[1..] : phase;
        return $"{name} · {title}";
    }

    private static string ListOrNone(IEnumerable<string> entries) => entries.ToList() is { Count: > 0 } list ? string.Join("\n", list) : "None";

    private static string Text(DislElement element, string attribute) =>
        element.Attributes.TryGetValue(attribute, out var value) ? value as string ?? value?.ToString() ?? "" : "";
}
