using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// A timeline's DISL model (DISL §4), built from what <see cref="TimelineParser"/> read, and where each
/// of its elements is declared.
/// </summary>
/// <param name="Diagram">The model.</param>
/// <param name="Declarations">Each element's and relation's place among the declarations, elements first, by the line it starts on.</param>
internal sealed record TimelineDislModel(DislDiagram Diagram, IReadOnlyDictionary<int, int> Declarations);

/// <summary>
/// Builds the DISL model of a <see cref="TimelineModel"/>: from the module's own reader rather than
/// through FBL, because FBL's YAML reading types a plain scalar (<c>1.0</c>, <c>null</c>, <c>yes</c>)
/// while a timeline keeps every value as the text it was written with.
/// </summary>
/// <remarks>
/// <para>
/// <b>An element with an <c>end</c> key is a Period, one without a Moment</b>, as the parser decides.
/// Every value is the text the parser kept: a begin or end that does not read stays as written, for
/// the rules to report, and the row is the integer the parser read.
/// </para>
/// <para>
/// <b>Model ids are unique; written ids need not be.</b> The first declaration with a written id keeps
/// it, and a later one, or one written without an id, gets an ephemeral id no declaration is written
/// with. The written id travels beside the model as the host attribute <see cref="WrittenId"/>, which
/// findings name an element by (§11.5.4) and lookups find it by, as <see cref="TimelineEdits"/> does.
/// </para>
/// <para>
/// <b>A relation end resolves to the first element written with that id</b>, never to a relation; an
/// end that names none is null, and its text is kept as <see cref="WrittenFrom"/> and
/// <see cref="WrittenTo"/> for <c>writtenEnd</c> (<see cref="TimelineTimes"/>).
/// </para>
/// </remarks>
internal static class TimelineDisl
{
    /// <summary>The host attribute holding the id an element or relation is written with, empty for none.</summary>
    public const string WrittenId = "writtenId";

    /// <summary>The host attribute holding a relation's <c>from</c> as written.</summary>
    public const string WrittenFrom = "writtenFrom";

    /// <summary>The host attribute holding a relation's <c>to</c> as written.</summary>
    public const string WrittenTo = "writtenTo";

    private static readonly ConditionalWeakTable<TimelineModel, TimelineDislModel> Built = [];

    /// <summary>The model of <paramref name="model"/>, built once per parsed model; read it, never change it.</summary>
    public static TimelineDislModel Of(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Built.GetValue(model, Build);
    }

    /// <summary>A model of <paramref name="model"/> of its own, for an operation to change as it runs.</summary>
    public static TimelineDislModel Build(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var written = model.Elements.Select(element => element.Id).Concat(model.Connections.Select(connection => connection.Id)).ToHashSet(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var ephemeral = 0;
        string ModelId(string id)
        {
            if (id.Length > 0 && taken.Add(id)) return id;
            string unnamed;
            do unnamed = $"timeline:unnamed:{++ephemeral}";
            while (written.Contains(unnamed) || !taken.Add(unnamed));
            return unnamed;
        }

        var elementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new Dictionary<int, int>();
        var read = new List<FblElement>();
        foreach (var element in model.Elements)
        {
            var id = ModelId(element.Id);
            elementIds.TryAdd(element.Id, id);
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = element.Label,
                ["begin"] = element.Begin.Text,
                ["row"] = (long)element.Row,
                [WrittenId] = element.Id,
            };
            if (element.End is { } end) attributes["end"] = end.Text;
            read.Add(new FblElement(id, true, element.IsPeriod ? "Period" : "Moment", "element", false, attributes, null, null, null, null, default, element.Range.Start + 1));
            declarations.TryAdd(element.Range.Start + 1, declarations.Count);
        }

        foreach (var connection in model.Connections)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = connection.Label,
                [WrittenId] = connection.Id,
                [WrittenFrom] = connection.From,
                [WrittenTo] = connection.To,
            };
            read.Add(new FblElement(
                ModelId(connection.Id), true, "Connection", "connection", true, attributes, null, null,
                elementIds.GetValueOrDefault(connection.From), elementIds.GetValueOrDefault(connection.To), default, connection.Range.Start + 1));
            declarations.TryAdd(connection.Range.Start + 1, declarations.Count);
        }

        var built = DislModelBuilder.From(new FblModel(read, [], false), TimelineDefinition.Specification);
        return new TimelineDislModel(built.Diagram, declarations);
    }

    /// <summary>The id <paramref name="element"/> is written with; empty for none.</summary>
    public static string WrittenIdOf(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.HostAttributes.TryGetValue(WrittenId, out var id) ? id as string ?? "" : "";
    }

    /// <summary>The element, else the relation, written with <paramref name="id"/>, as <see cref="TimelineEdits"/> finds it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        if (id is null) return null;
        return diagram.Nodes.FirstOrDefault(node => WrittenIdOf(node) == id)
            ?? diagram.Relations.FirstOrDefault(relation => WrittenIdOf(relation) == id);
    }
}
