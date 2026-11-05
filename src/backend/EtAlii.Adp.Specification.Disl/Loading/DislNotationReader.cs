using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads what the derivation needs of <c>notation</c> and <c>viewpoints</c> (DISL §6, §3.5) into <see cref="DislNotation"/>
/// and <see cref="DislViewpoints"/>: the labels it edits inline, the tool a connect gesture runs, the canvases' chrome,
/// the viewpoints' membership and the type names both declare a notation for.
/// </summary>
/// <remarks>
/// Reading never refuses: what the runtime threw on, skipped or read as absent when it read the JSON at run time is
/// read as a record that does the same when it is used.
/// </remarks>
internal static class DislNotationReader
{
    public static DislNotation Read(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("notation", out var notation)) return DislNotation.None;

        var labels = new List<(string Type, DislTypeLabels Labels)>();
        var refusals = new List<(string Type, bool IsEdge, IReadOnlyDictionary<string, IReadOnlyList<DislReason>> Gestures)>();
        foreach (var kind in new[] { "nodes", "edges" })
        {
            foreach (var type in DislJson.Members(notation, kind))
            {
                var pointer = JsonPointer.Append(JsonPointer.Append("/notation", kind), type.Name);
                labels.Add((type.Name, Labels(specification, type.Value, pointer)));
                refusals.Add((type.Name, kind == "edges", Refusals(specification, type.Value, JsonPointer.Append(pointer, "refusals"))));
            }
        }

        var malformed = DislFieldReader.NotAnObject(notation);
        List<(string Type, DislEdgeNotation Edge)>? edges = null;
        string? edgesMalformed = null;
        if (malformed is null && notation.TryGetProperty("edges", out var declared))
        {
            edgesMalformed = DislFieldReader.NotAnObject(declared);
            edges = edgesMalformed is null ? [.. declared.EnumerateObject().Select(edge => (edge.Name, Edge(edge.Value)))] : [];
        }

        var canvas = malformed is null && notation.TryGetProperty("canvas", out var declaredCanvas) && declaredCanvas.ValueKind == JsonValueKind.Object
            ? Canvas(specification, declaredCanvas, "/notation/canvas")
            : null;
        return new DislNotation(Names(notation), labels, edges, edgesMalformed, canvas, malformed, refusals);
    }

    /// <summary>A node or edge notation's <c>refusals</c> (§6.9): per gesture its Reason[]; one Reason that is not in a list is read as a list of it.</summary>
    private static Dictionary<string, IReadOnlyList<DislReason>> Refusals(DislSpecification specification, JsonElement type, string pointer)
    {
        var gestures = new Dictionary<string, IReadOnlyList<DislReason>>(StringComparer.Ordinal);
        if (type.ValueKind != JsonValueKind.Object || !type.TryGetProperty("refusals", out var refusals) || refusals.ValueKind != JsonValueKind.Object) return gestures;
        foreach (var gesture in refusals.EnumerateObject())
        {
            var at = JsonPointer.Append(pointer, gesture.Name);
            gestures[gesture.Name] = gesture.Value.ValueKind == JsonValueKind.Array
                ? DislFieldReader.Reasons(specification, gesture.Value, at)
                : [DislFieldReader.Reason(specification, gesture.Value, at)];
        }
        return gestures;
    }

    public static DislViewpoints Viewpoints(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("viewpoints", out var viewpoints)) return DislViewpoints.None;
        return new DislViewpoints(
            viewpoints.ValueKind == JsonValueKind.Object,
            [.. DislJson.Members(root, "viewpoints").Select(viewpoint => (viewpoint.Name, Viewpoint(specification, viewpoint.Value, JsonPointer.Append("/viewpoints", viewpoint.Name))))]);
    }

    private static DislViewpoint Viewpoint(DislSpecification specification, JsonElement viewpoint, string pointer)
    {
        var include = JsonProperties.StringOrStrings(viewpoint, "include");
        var exclude = JsonProperties.StringOrStrings(viewpoint, "exclude");
        if (DislFieldReader.NotAnObject(viewpoint) is { } malformed) return new DislViewpoint(include, exclude, null, null, null, malformed);
        return new DislViewpoint(
            include,
            exclude,
            specification.ExpressionAt(JsonPointer.Append(pointer, "members")),
            viewpoint.TryGetProperty("canvas", out var canvas) && canvas.ValueKind == JsonValueKind.Object ? Canvas(specification, canvas, JsonPointer.Append(pointer, "canvas")) : null,
            viewpoint.TryGetProperty("notation", out var notation) ? Names(notation) : null,
            null);
    }

    private static DislNotationNames Names(JsonElement notation) =>
        new([.. DislJson.Members(notation, "nodes").Select(node => node.Name)], [.. DislJson.Members(notation, "edges").Select(edge => edge.Name)]);

    private static DislTypeLabels Labels(DislSpecification specification, JsonElement type, string pointer)
    {
        if (DislFieldReader.NotAnObject(type) is { } malformed) return new DislTypeLabels(null, malformed);
        if (!type.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array) return new DislTypeLabels(null, null);

        var list = new List<DislLabel>();
        var index = 0;
        foreach (var label in labels.EnumerateArray())
        {
            var at = JsonPointer.Append(JsonPointer.Append(pointer, "labels"), index++);
            if (label.ValueKind != JsonValueKind.Object) continue;
            var key = label.TryGetProperty("editText", out _) ? "editText" : "text";
            list.Add(new DislLabel(
                JsonProperties.String(label, "id"),
                label.TryGetProperty(key, out var text) ? EditText(specification, key, text, JsonPointer.Append(at, key)) : null,
                label.TryGetProperty("parse", out var parse) ? DislFormReader.Parse(specification, parse, JsonPointer.Append(at, "parse")) : null));
        }
        return new DislTypeLabels(list, null);
    }

    /// <summary>
    /// The text a label's inline editor opens on (§6.12): its <c>editText</c>, a Message or an <c>{attribute}</c>, else
    /// its <c>text</c>, a Bindable (<paramref name="key"/> says which). A bare string is literal text in both: a
    /// Bindable's is always a literal (§2.5 b, behaviour change B-40), and a Message's is a LocalizedText (§2.3, B-41).
    /// An object with <c>cel</c> is an Expression, an <c>{attribute}</c> binding the value of that attribute (B-42), and
    /// any other object in <c>editText</c> a LocalizedText map, its locale's text (§2.3, B-42); anything else is an
    /// Expression without source, which opens on no text.
    /// </summary>
    private static DislBindable EditText(DislSpecification specification, string key, JsonElement text, string pointer) => text.ValueKind switch
    {
        JsonValueKind.String => new DislBindable.Literal(text.GetString()),
        JsonValueKind.Object when text.TryGetProperty("cel", out _) => new DislBindable.Expression(DislFieldReader.Expression(specification, text, pointer)),
        JsonValueKind.Object when JsonProperties.String(text, "attribute") is { } attribute => new DislBindable.Attribute(attribute),
        JsonValueKind.Object when key == "editText" => new DislBindable.Literal(DislFieldReader.LocalizedText(text)),
        _ => new DislBindable.Expression(DislFieldReader.Expression(specification, text, pointer)),
    };

    private static DislEdgeNotation Edge(JsonElement edge) =>
        DislFieldReader.NotAnObject(edge) is { } malformed
            ? new DislEdgeNotation(null, malformed)
            : new DislEdgeNotation(edge.TryGetProperty("connect", out var connect) ? JsonProperties.String(connect, "tool") : null, null);

    private static DislCanvas Canvas(DislSpecification specification, JsonElement canvas, string pointer) =>
        new(
            canvas.TryGetProperty("title", out var title) ? Label(specification, title, JsonPointer.Append(pointer, "title")) : null,
            canvas.TryGetProperty("header", out var header) ? Band(specification, header, JsonPointer.Append(pointer, "header")) : null,
            canvas.TryGetProperty("legend", out var legend) ? Legend(specification, legend, JsonPointer.Append(pointer, "legend")) : null);

    private static DislCanvasLabel Label(DislSpecification specification, JsonElement label, string pointer)
    {
        if (label.ValueKind != JsonValueKind.Object) return new DislCanvasLabel(false, new DislBindable.Literal(false), new DislBindable.Literal(""));
        var at = JsonPointer.Append(pointer, "text");
        DislBindable text = !label.TryGetProperty("text", out var declared) ? new DislBindable.Literal("")
            : declared.ValueKind == JsonValueKind.String ? new DislBindable.Literal(declared.GetString())
            : declared.ValueKind == JsonValueKind.Object && JsonProperties.String(declared, "attribute") is { } attribute ? new DislBindable.Attribute(attribute)
            : declared.ValueKind == JsonValueKind.Object && declared.TryGetProperty("cel", out _) ? new DislBindable.Expression(DislFieldReader.Expression(specification, declared, at))
            : new DislBindable.Literal("");
        return new DislCanvasLabel(true, Visible(specification, label, pointer), text);
    }

    /// <summary>A Bindable <c>visible</c>: true when absent, a literal as it is, an <c>{attribute}</c>, else an Expression.</summary>
    private static DislBindable Visible(DislSpecification specification, JsonElement owner, string pointer)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty("visible", out var visible)) return new DislBindable.Literal(true);
        return visible.ValueKind switch
        {
            JsonValueKind.True => new DislBindable.Literal(true),
            JsonValueKind.False => new DislBindable.Literal(false),
            JsonValueKind.Object when JsonProperties.String(visible, "attribute") is { } attribute => new DislBindable.Attribute(attribute),
            _ => new DislBindable.Expression(DislFieldReader.Expression(specification, visible, JsonPointer.Append(pointer, "visible"))),
        };
    }

    private static DislCanvasBand Band(DislSpecification specification, JsonElement band, string pointer)
    {
        var visible = Visible(specification, band, pointer);
        if (DislFieldReader.NotAnObject(band) is { } malformed) return new DislCanvasBand(visible, null, malformed);
        if (!band.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return new DislCanvasBand(visible, null, null);
        var index = 0;
        return new DislCanvasBand(visible, [.. rows.EnumerateArray().Select(row => Label(specification, row, JsonPointer.Append(JsonPointer.Append(pointer, "rows"), index++)))], null);
    }

    private static DislCanvasLegend Legend(DislSpecification specification, JsonElement legend, string pointer)
    {
        if (legend.ValueKind != JsonValueKind.Object) return new DislCanvasLegend(false, null, false, [], null, null);
        var computedAt = JsonPointer.Append(pointer, "computed");
        var computed = legend.TryGetProperty("computed", out var declared) && declared.ValueKind == JsonValueKind.Object && declared.TryGetProperty("key", out var key)
            ? new DislLegendComputed(
                DislFieldReader.Expression(specification, key, JsonPointer.Append(computedAt, "key")),
                DislFieldReader.OptionalExpression(specification, declared, computedAt, "order"),
                DislFieldReader.OptionalMessage(specification, declared, computedAt, "label"))
            : null;
        return new DislCanvasLegend(
            !(legend.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False),
            DislFieldReader.OptionalMessage(specification, legend, pointer, "title"),
            JsonProperties.String(legend, "from") == "drawn",
            JsonProperties.StringOrStrings(legend, "entries"),
            computed,
            JsonProperties.String(legend, "position"));
    }
}
