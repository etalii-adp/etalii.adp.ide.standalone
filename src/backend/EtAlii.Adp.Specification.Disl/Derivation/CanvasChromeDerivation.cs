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

        var title = Property(specification, env, canvas => canvas.Title) is { } titleLabel ? Label(titleLabel, variables) : null;

        List<string> header = [];
        if (Property(specification, env, canvas => canvas.Header) is { } band && Visible(band.Visible, variables))
        {
            // A band that is not an object threw here when it was read from the JSON at run time, and still does.
            if (band.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            foreach (var row in band.Rows ?? [])
            {
                if (Label(row, variables) is { } text) header.Add(text);
            }
        }

        var legend = Property(specification, env, canvas => canvas.Legend) is { } declared ? Legend(diagram, declared, drawn, chrome, cel) : null;
        return new DerivedCanvasChrome(title, header, legend);
    }

    /// <summary>A canvas property: the active viewpoint's, else the notation's; null when neither states it.</summary>
    private static T? Property<T>(DislSpecification specification, DislEnv env, Func<DislCanvas, T?> property)
        where T : class
    {
        if (env.Viewpoint.Length > 0
            && specification.Viewpoints.Find(env.Viewpoint) is { Canvas: { } own }
            && property(own) is { } value)
        {
            return value;
        }
        return specification.Notation.Canvas is { } canvas ? property(canvas) : null;
    }

    /// <summary>A Label's text (§6.12) with <c>self</c> the diagram, or null when it is not visible.</summary>
    private static string? Label(DislCanvasLabel label, IReadOnlyDictionary<string, object?> variables)
    {
        if (!label.IsObject || !Visible(label.Visible, variables)) return null;
        return label.Text switch
        {
            DislBindable.Literal literal => literal.Value as string ?? "",
            DislBindable.Attribute attribute => ((ICelObject)variables["self"]!).TryGetMember(attribute.Name, out var value) ? DislEvaluation.TextOf(value) : "",
            DislBindable.Expression expression => expression.Field.Evaluate(variables) is var value and not CelError ? DislEvaluation.TextOf(value) : "",
            _ => "",
        };
    }

    /// <summary>Whether a Bindable <c>visible</c> holds: true when absent, a literal as it is, a <c>{cel}</c> while it is true.</summary>
    private static bool Visible(DislBindable visible, IReadOnlyDictionary<string, object?> variables) => visible switch
    {
        DislBindable.Literal literal => literal.Value is true,
        DislBindable.Attribute attribute => ((ICelObject)variables["self"]!).TryGetMember(attribute.Name, out var value) && value is true,
        DislBindable.Expression expression => expression.Field.Holds(variables),
        _ => true,
    };

    private static DerivedLegend? Legend(DislDiagram diagram, DislCanvasLegend legend, IReadOnlyList<DislElement> drawn, DislDrawnDiagram chrome, CelMap env)
    {
        if (!legend.Shown) return null;

        var specification = diagram.Specification;
        var onDiagram = new Dictionary<string, object?> { ["self"] = chrome, ["diagram"] = chrome, ["env"] = env };
        var title = legend.Title?.Text(onDiagram, "") ?? "";

        // First appearance in model order (§6.13): the drawn elements as the model orders them, any the model does not hold after them as given.
        var order = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        foreach (var element in diagram.Elements) order.TryAdd(element, order.Count);
        var inModelOrder = drawn.Select((element, index) => (element, index)).OrderBy(entry => order.TryGetValue(entry.element, out var at) ? at : order.Count + entry.index).Select(entry => entry.element).ToList();

        List<DerivedLegendEntry> entries = [];
        foreach (var entry in legend.Entries)
        {
            var first = inModelOrder.FirstOrDefault(element => Matches(element, entry));
            if (legend.FromDrawn && first is null) continue;
            entries.Add(new DerivedLegendEntry(entry, DeclaredLabel(specification, entry), first));
        }

        if (legend.Computed is { } computed)
        {
            var groups = new List<(string Key, DislElement First)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in inModelOrder)
            {
                var value = computed.Key.Evaluate(OnElement(element));
                if (value is CelError) continue;
                var text = DislEvaluation.TextOf(value);
                if (seen.Add(text)) groups.Add((text, element));
            }
            if (computed.Order is { } sort)
            {
                groups = [.. groups.OrderBy(group => sort.Evaluate(OnElement(group.First)), SortKey.Instance)];
            }
            foreach ((string text, DislElement first) in groups)
            {
                var label = computed.Label?.Text(OnElement(first), text) ?? text;
                entries.Add(new DerivedLegendEntry(text, label, first));
            }
        }

        return new DerivedLegend(title, legend.Position, entries);

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
        if (specification.Metamodel.TypeOf(entry) is { } type) return type.Label ?? type.Name;
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
