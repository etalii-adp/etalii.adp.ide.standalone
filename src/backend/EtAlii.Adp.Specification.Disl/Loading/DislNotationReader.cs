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
        foreach (var kind in new[] { "nodes", "edges" })
        {
            foreach (var type in DislJson.Members(notation, kind)) labels.Add((type.Name, Labels(specification, type.Value, JsonPointer.Append(JsonPointer.Append("/notation", kind), type.Name))));
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
        return new DislNotation(Names(notation), labels, edges, edgesMalformed, canvas, malformed);
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
    /// The text a label's inline editor opens on. Its <c>text</c> is a Bindable (§2.5 b), whose bare string is always a
    /// literal, so the editor opens on that string itself (behaviour change B-40). A bare string in <c>editText</c>
    /// (<paramref name="key"/>) is compiled in the element context with the entered text's <c>value</c> bound, as the
    /// runtime always read it, and opens on no text when it does not compile; any other value is an Expression.
    /// </summary>
    private static DislBindable EditText(DislSpecification specification, string key, JsonElement text, string pointer)
    {
        if (text.ValueKind != JsonValueKind.String) return new DislBindable.Expression(DislFieldReader.Expression(specification, text, pointer));
        var source = text.GetString()!;
        if (key == "text") return new DislBindable.Literal(source);
        return new DislBindable.Expression(new DislExpressionField(pointer, source, specification.Programs.TryGet(DislProgramKey.Of(DislContext.Element, source, ["value"]), out var program, out _) ? program : null));
    }

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
                declared.TryGetProperty("order", out var order) ? DislFieldReader.Expression(specification, order, JsonPointer.Append(computedAt, "order")) : null,
                declared.TryGetProperty("label", out var label) ? DislFieldReader.Message(specification, label, JsonPointer.Append(computedAt, "label")) : null)
            : null;
        return new DislCanvasLegend(
            !(legend.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False),
            legend.TryGetProperty("title", out var title) ? DislFieldReader.Message(specification, title, JsonPointer.Append(pointer, "title")) : null,
            JsonProperties.String(legend, "from") == "drawn",
            JsonProperties.StringOrStrings(legend, "entries"),
            computed,
            JsonProperties.String(legend, "position"));
    }
}
