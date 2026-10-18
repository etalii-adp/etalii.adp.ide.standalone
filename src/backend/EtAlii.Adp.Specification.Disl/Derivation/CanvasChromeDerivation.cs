using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>One entry of a legend (DISL §6.13).</summary>
/// <param name="Key">What groups it: a declared entry's string, or a computed entry's <c>key</c> as text.</param>
/// <param name="Label">Its text: a computed entry's <c>label</c> on the first element of its group, else its key; a declared entry's type or enum value label, else the string.</param>
/// <param name="First">
/// The first drawn element it stands for, in model order, from which a host paints its swatch; null for
/// a declared entry that no drawn element matches.
/// </param>
public sealed record DerivedLegendEntry(string Key, string Label, DislElement? First);

/// <summary>A canvas's legend as a host shows it (DISL §6.13).</summary>
/// <param name="Title">Its heading, <c>legend.title</c>; empty when it states none.</param>
/// <param name="Position">Its <c>position</c> when it is one word (<c>outside-bottom</c>), else null.</param>
/// <param name="Entries">Its entries: the declared ones in declaration order, then the computed ones.</param>
public sealed record DerivedLegend(string Title, string? Position, IReadOnlyList<DerivedLegendEntry> Entries);

/// <summary>The chrome of a canvas the derivation answers (DISL §6.13): its title, its header band and its legend.</summary>
/// <param name="Title">The title's text, or null when the canvas states no title or it is not visible.</param>
/// <param name="Header">The header band's rows that are visible, in order; empty when it states none or the band is not visible.</param>
/// <param name="Legend">The legend, or null when the canvas states none or it is not visible.</param>
public sealed record DerivedCanvasChrome(string? Title, IReadOnlyList<string> Header, DerivedLegend? Legend);

/// <summary>
/// A canvas's title, header band and legend (DISL §6.13), for the elements a host draws: the texts a
/// host shows outside the drawing surface, evaluated on the backend.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which canvas.</b> The active viewpoint's <c>canvas</c> (§3.5), property by property, over
/// <c>notation.canvas</c>: a viewpoint that states a title of its own keeps the notation's legend.
/// </para>
/// <para>
/// <b>What is drawn</b> is the host's: <paramref name="drawn"/> of <see cref="Derive"/> is the result of
/// §6.1's steps, which depend on viewer state this runtime does not hold. It is what <c>diagram.drawn</c>
/// reads in these texts, and what a legend with <c>from: "drawn"</c> and a computed legend count.
/// </para>
/// <para>
/// <b>The title and the header rows</b> are Labels with <c>self</c> bound to the diagram: a literal text,
/// a <c>{cel}</c> text, or an <c>{attribute}</c> binding of a diagram attribute; a label whose
/// <c>visible</c> fails is not shown. <b>A legend</b> lists its declared <c>entries</c>, all of them with
/// <c>from: "declared"</c> (the default) and, with <c>from: "drawn"</c>, those a drawn element matches by
/// its type or by an enum value it takes; then its <c>computed</c> entries: the drawn elements grouped by
/// <c>key</c>, one entry per group, labelled on the group's first element and ordered by first appearance
/// in model order, or by <c>order</c> evaluated on that first element.
/// </para>
/// <para>
/// <b>Not answered here</b>: a swatch's paints, which name theme tokens only the drawing side resolves
/// (<c>token()</c> refuses on this runtime), and the declared entries that name a marker or a
/// conditional style, which need the resolved notation. A host paints the swatch from
/// <see cref="DerivedLegendEntry.First"/>. A text that fails gives its fallback: a title or a row its
/// empty text, a computed entry its key; an element whose key fails is in no entry.
/// </para>
/// </remarks>
public static class CanvasChromeDerivation
{
    public static DerivedCanvasChrome Derive(DislDiagram diagram, IReadOnlyList<DislElement> drawn, DislEnv env)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(env);

        var specification = diagram.Specification;
        var chrome = new DislDrawnDiagram(diagram, drawn);
        var cel = env.ToCel();
        var variables = new Dictionary<string, object?> { ["self"] = chrome, ["diagram"] = chrome, ["env"] = cel };

        var title = Property(specification, env, "title") is var (titleJson, titlePointer) ? Label(specification, titleJson, titlePointer, variables) : null;

        List<string> header = [];
        if (Property(specification, env, "header") is var (band, bandPointer) && Visible(specification, band, bandPointer, variables) && band.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var row in rows.EnumerateArray())
            {
                if (Label(specification, row, DislJson.Pointer(DislJson.Pointer(bandPointer, "rows"), index++), variables) is { } text) header.Add(text);
            }
        }

        var legend = Property(specification, env, "legend") is var (legendJson, legendPointer) ? Legend(diagram, legendJson, legendPointer, drawn, chrome, cel) : null;
        return new DerivedCanvasChrome(title, header, legend);
    }

    /// <summary>The canvas property <paramref name="name"/>: the active viewpoint's, else the notation's; null when neither states it.</summary>
    private static (JsonElement Json, string Pointer)? Property(DislSpecification specification, DislEnv env, string name)
    {
        var root = specification.Root;
        if (env.Viewpoint.Length > 0
            && root.TryGetProperty("viewpoints", out var viewpoints) && viewpoints.ValueKind == JsonValueKind.Object
            && viewpoints.TryGetProperty(env.Viewpoint, out var viewpoint) && viewpoint.ValueKind == JsonValueKind.Object
            && viewpoint.TryGetProperty("canvas", out var own) && own.ValueKind == JsonValueKind.Object && own.TryGetProperty(name, out var value))
        {
            return (value, DislJson.Pointer(DislJson.Pointer(DislJson.Pointer("/viewpoints", env.Viewpoint), "canvas"), name));
        }
        if (root.TryGetProperty("notation", out var notation) && notation.ValueKind == JsonValueKind.Object
            && notation.TryGetProperty("canvas", out var canvas) && canvas.ValueKind == JsonValueKind.Object && canvas.TryGetProperty(name, out var declared))
        {
            return (declared, DislJson.Pointer("/notation/canvas", name));
        }
        return null;
    }

    /// <summary>A Label's text (§6.12) with <c>self</c> the diagram, or null when it is not visible.</summary>
    private static string? Label(DislSpecification specification, JsonElement label, string pointer, IReadOnlyDictionary<string, object?> variables)
    {
        if (label.ValueKind != JsonValueKind.Object || !Visible(specification, label, pointer, variables)) return null;
        if (!label.TryGetProperty("text", out var text)) return "";
        var at = DislJson.Pointer(pointer, "text");
        return text.ValueKind switch
        {
            JsonValueKind.String => text.GetString()!,
            JsonValueKind.Object when DislJson.String(text, "attribute") is { } attribute => ((ICelObject)variables["self"]!).TryGetMember(attribute, out var value) ? DislEvaluation.TextOf(value) : "",
            JsonValueKind.Object when text.TryGetProperty("cel", out _) => DislEvaluation.Expression(specification, text, at, DislContexts.Element, variables) is var value and not CelError ? DislEvaluation.TextOf(value) : "",
            _ => "",
        };
    }

    /// <summary>Whether a Bindable <c>visible</c> holds: true when absent, a literal as it is, a <c>{cel}</c> while it is true.</summary>
    private static bool Visible(DislSpecification specification, JsonElement owner, string pointer, IReadOnlyDictionary<string, object?> variables)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty("visible", out var visible)) return true;
        return visible.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object when DislJson.String(visible, "attribute") is { } attribute => ((ICelObject)variables["self"]!).TryGetMember(attribute, out var value) && value is true,
            _ => DislEvaluation.Holds(specification, visible, DislJson.Pointer(pointer, "visible"), DislContexts.Element, variables),
        };
    }

    private static DerivedLegend? Legend(DislDiagram diagram, JsonElement legend, string pointer, IReadOnlyList<DislElement> drawn, DislDrawnDiagram chrome, CelMap env)
    {
        if (legend.ValueKind != JsonValueKind.Object || legend.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False) return null;

        var specification = diagram.Specification;
        var onDiagram = new Dictionary<string, object?> { ["self"] = chrome, ["diagram"] = chrome, ["env"] = env };
        var title = legend.TryGetProperty("title", out var heading)
            ? DislEvaluation.Message(specification, heading, DislJson.Pointer(pointer, "title"), DislContexts.Legend, onDiagram, "")
            : "";

        // First appearance in model order (§6.13): the drawn elements as the model orders them, any the model does not hold after them as given.
        var order = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        foreach (var element in diagram.Elements) order.TryAdd(element, order.Count);
        var inModelOrder = drawn.Select((element, index) => (element, index)).OrderBy(entry => order.TryGetValue(entry.element, out var at) ? at : order.Count + entry.index).Select(entry => entry.element).ToList();

        List<DerivedLegendEntry> entries = [];
        var fromDrawn = DislJson.String(legend, "from") == "drawn";
        foreach (var entry in DislJson.Strings(legend, "entries"))
        {
            var first = inModelOrder.FirstOrDefault(element => Matches(element, entry));
            if (fromDrawn && first is null) continue;
            entries.Add(new DerivedLegendEntry(entry, DeclaredLabel(specification, entry), first));
        }

        if (legend.TryGetProperty("computed", out var computed) && computed.ValueKind == JsonValueKind.Object && computed.TryGetProperty("key", out var key))
        {
            var at = DislJson.Pointer(pointer, "computed");
            var groups = new List<(string Key, DislElement First)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in inModelOrder)
            {
                var value = DislEvaluation.Expression(specification, key, DislJson.Pointer(at, "key"), DislContexts.Legend, OnElement(element));
                if (value is CelError) continue;
                var text = DislEvaluation.TextOf(value);
                if (seen.Add(text)) groups.Add((text, element));
            }
            if (computed.TryGetProperty("order", out var sort))
            {
                groups = [.. groups.OrderBy(group => DislEvaluation.Expression(specification, sort, DislJson.Pointer(at, "order"), DislContexts.Legend, OnElement(group.First)), SortKey.Instance)];
            }
            foreach ((string text, DislElement first) in groups)
            {
                var label = computed.TryGetProperty("label", out var message)
                    ? DislEvaluation.Message(specification, message, DislJson.Pointer(at, "label"), DislContexts.Legend, OnElement(first), text)
                    : text;
                entries.Add(new DerivedLegendEntry(text, label, first));
            }
        }

        var position = DislJson.String(legend, "position");
        return new DerivedLegend(title, position, entries);

        Dictionary<string, object?> OnElement(DislElement element) => new() { ["self"] = element, ["diagram"] = chrome, ["env"] = env };
    }

    /// <summary>Whether a drawn element matches a declared entry: it has that type, or an attribute of an enum type takes that value.</summary>
    private static bool Matches(DislElement element, string entry) =>
        element.IsA(entry)
        || element.Type.Attributes.Values.Any(attribute => element.Diagram.Specification.Metamodel.Enums.ContainsKey(attribute.Type) && Takes(element.ValueOf(attribute.Name), entry));

    private static bool Takes(object? value, string entry) => value switch
    {
        string text => text == entry,
        IReadOnlyList<object?> values => values.Any(item => item as string == entry),
        _ => false,
    };

    /// <summary>A declared entry's text: the label of the type it names, else of the enum value, else the entry itself.</summary>
    private static string DeclaredLabel(DislSpecification specification, string entry)
    {
        if (specification.Metamodel.TypeOf(entry) is { } type) return DislJson.String(type.Json, "label") ?? type.Name;
        var value = specification.Metamodel.Enums.Values.SelectMany(enumeration => enumeration.Values).FirstOrDefault(candidate => candidate.Key == entry);
        return value?.Label ?? entry;
    }

    /// <summary>Orders the values of a legend's <c>order</c>: numbers by value, before texts by code point, before anything else; a failed value last.</summary>
    private sealed class SortKey : IComparer<object?>
    {
        public static SortKey Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            var rank = Rank(x).CompareTo(Rank(y));
            if (rank != 0) return rank;
            return (x, y) switch
            {
                (long a, long b) => a.CompareTo(b),
                (long or double, long or double) => Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture)),
                (string a, string b) => string.CompareOrdinal(a, b),
                _ => 0,
            };
        }

        private static int Rank(object? value) => value switch
        {
            long or double => 0,
            string => 1,
            CelError => 3,
            _ => 2,
        };
    }
}

/// <summary>
/// The diagram as a canvas's chrome sees it (DISL §6.13): the diagram, with <c>diagram.drawn</c>, the
/// elements the host draws, which the deterministic contexts never read (§12.5).
/// </summary>
internal sealed class DislDrawnDiagram(DislDiagram diagram, IReadOnlyList<DislElement> drawn) : ICelObject
{
    public bool TryGetMember(string name, out object? value)
    {
        if (name == "drawn")
        {
            value = drawn.Cast<object?>().ToList();
            return true;
        }
        return diagram.TryGetMember(name, out value);
    }

    public bool HasMember(string name) => name == "drawn" || diagram.HasMember(name);

    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value) => diagram.TryInvoke(name, arguments, out value);

    public override string ToString() => diagram.ToString();
}
