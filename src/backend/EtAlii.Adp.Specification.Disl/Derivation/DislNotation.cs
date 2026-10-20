namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What the derivation reads of <c>notation</c> (DISL §6), typed at load: the labels it edits inline, the tool a
/// connect gesture runs, the canvas's chrome, and the type names it declares a notation for.
/// </summary>
/// <param name="Names">The node and edge notations' names.</param>
/// <param name="Labels">Per node notation, then per edge notation, in declaration order: its labels.</param>
/// <param name="Edges">The edge notations by name, in declaration order; null when <c>edges</c> is absent.</param>
/// <param name="EdgesMalformed">The message System.Text.Json threw looking a name up in <c>edges</c> that is not an object, null otherwise.</param>
/// <param name="Canvas">The <c>canvas</c>, or null when it is absent or the notation or it is not an object.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime read a member of a notation that is not an object, null for one
/// that is; such a notation still throws it where its <c>edges</c> were read.
/// </param>
internal sealed record DislNotation(
    DislNotationNames Names,
    IReadOnlyList<(string Type, DislTypeLabels Labels)> Labels,
    IReadOnlyList<(string Type, DislEdgeNotation Edge)>? Edges,
    string? EdgesMalformed,
    DislCanvas? Canvas,
    string? NotAnObject)
{
    public static DislNotation None { get; } = new(DislNotationNames.None, [], null, null, null, null);

    /// <summary>The tool <c>connect.tool</c> of the edge notation of <paramref name="relationType"/> names (§6.10), or null when it names none.</summary>
    public string? ConnectToolOf(string relationType)
    {
        if (NotAnObject is not null) throw new InvalidOperationException(NotAnObject);
        if (Edges is null) return null;
        if (EdgesMalformed is not null) throw new InvalidOperationException(EdgesMalformed);
        if (Edges.LastOrDefault(entry => entry.Type == relationType).Edge is not { } edge) return null;
        return edge.NotAnObject is not null ? throw new InvalidOperationException(edge.NotAnObject) : edge.ConnectTool;
    }

    /// <summary>The label <paramref name="id"/> of the notation of <paramref name="type"/>: a node's, else a relation's; null when there is none.</summary>
    public DislLabel? LabelOf(string type, string id)
    {
        foreach ((string declared, DislTypeLabels labels) in Labels)
        {
            if (declared != type) continue;
            if (labels.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            if (labels.Labels?.FirstOrDefault(label => label.Id == id) is { } found) return found;
        }
        return null;
    }
}

/// <summary>The names a notation declares a node notation and an edge notation for, in declaration order.</summary>
internal sealed record DislNotationNames(IReadOnlyList<string> Nodes, IReadOnlyList<string> Edges)
{
    public static DislNotationNames None { get; } = new([], []);
}

/// <summary>The labels of one node or edge notation (DISL §6.12), typed at load.</summary>
/// <param name="Labels">The labels that are objects; null when <c>labels</c> is absent or not a list.</param>
/// <param name="NotAnObject">The message System.Text.Json threw reading the labels of a notation that is not an object, null for one that is.</param>
internal sealed record DislTypeLabels(IReadOnlyList<DislLabel>? Labels, string? NotAnObject);

/// <summary>A label of a notation (DISL §6.12), as its inline editor reads it, typed at load.</summary>
/// <param name="Id">Its <c>id</c>.</param>
/// <param name="EditText">Its <c>editText</c>, else its <c>text</c>, read as the text the editor opens on; null when it states neither.</param>
/// <param name="Parse">Its <c>parse</c>, or null when it states none, so that it cannot be edited.</param>
internal sealed record DislLabel(string? Id, DislExpressionField? EditText, DislFormParse? Parse);

/// <summary>An edge notation (DISL §6.10), as a connect gesture reads it.</summary>
/// <param name="ConnectTool">The tool its <c>connect.tool</c> names, or null.</param>
/// <param name="NotAnObject">The message System.Text.Json threw reading the <c>connect</c> of a notation that is not an object, null for one that is.</param>
internal sealed record DislEdgeNotation(string? ConnectTool, string? NotAnObject);

/// <summary>
/// A canvas's chrome (DISL §6.13), typed at load: each property null when the canvas does not state it, so that a
/// viewpoint's canvas states it over the notation's property by property.
/// </summary>
internal sealed record DislCanvas(DislCanvasLabel? Title, DislCanvasBand? Header, DislCanvasLegend? Legend);

/// <summary>
/// A Bindable (DISL §2.5 b) of the chrome, typed at load: a literal, an <c>{attribute}</c> binding of the diagram, or an
/// Expression.
/// </summary>
internal abstract record DislBindable
{
    public sealed record Literal(object? Value) : DislBindable;

    public sealed record Attribute(string Name) : DislBindable;

    public sealed record Expression(DislExpressionField Field) : DislBindable;
}

/// <summary>A Label of the chrome (DISL §6.12): its visibility and its text; one that is not an object is not shown.</summary>
internal sealed record DislCanvasLabel(bool IsObject, DislBindable Visible, DislBindable Text);

/// <summary>The header band (DISL §6.13): its visibility and its rows, each a Label; null rows when it states none or they are not a list.</summary>
/// <param name="NotAnObject">The message System.Text.Json threw reading the rows of a band that is not an object, null for one that is.</param>
internal sealed record DislCanvasBand(DislBindable Visible, IReadOnlyList<DislCanvasLabel>? Rows, string? NotAnObject);

/// <summary>A legend (DISL §6.13), typed at load.</summary>
/// <param name="Shown">Whether it is shown: an object whose <c>visible</c> is not <c>false</c>.</param>
/// <param name="Title">Its <c>title</c>, or null when it states none.</param>
/// <param name="FromDrawn">Whether its <c>from</c> is <c>drawn</c>.</param>
/// <param name="Entries">Its declared <c>entries</c>.</param>
/// <param name="Computed">Its <c>computed</c> entries, or null when it states none with a <c>key</c>.</param>
/// <param name="Position">Its <c>position</c>, or null.</param>
internal sealed record DislCanvasLegend(bool Shown, DislMessage? Title, bool FromDrawn, IReadOnlyList<string> Entries, DislLegendComputed? Computed, string? Position);

/// <summary>A legend's <c>computed</c> entries (DISL §6.13): the key that groups the drawn elements, their order and their label.</summary>
internal sealed record DislLegendComputed(DislExpressionField Key, DislExpressionField? Order, DislMessage? Label);

/// <summary>The <c>viewpoints</c> of a specification (DISL §3.5), typed at load.</summary>
/// <param name="Declared">Whether <c>viewpoints</c> is an object; without one there is the implicit <c>main</c>, which shows everything.</param>
/// <param name="All">The viewpoints, by name, in declaration order.</param>
internal sealed record DislViewpoints(bool Declared, IReadOnlyList<(string Name, DislViewpoint Viewpoint)> All)
{
    public static DislViewpoints None { get; } = new(false, []);

    /// <summary>The viewpoint named <paramref name="name"/>, or null when there is none.</summary>
    public DislViewpoint? Find(string name) => All.LastOrDefault(entry => entry.Name == name).Viewpoint;
}

/// <summary>A viewpoint (DISL §3.5), typed at load.</summary>
/// <param name="Include">The types it shows; none for all.</param>
/// <param name="Exclude">The types it hides.</param>
/// <param name="Members">Its <c>members</c> condition as the loader compiled it at <c>members</c>, or null.</param>
/// <param name="Canvas">Its <c>canvas</c>, or null when it states none as an object.</param>
/// <param name="Notation">The names of its <c>notation</c> overrides, or null when it states none.</param>
/// <param name="NotAnObject">The message System.Text.Json threw reading the <c>notation</c> of a viewpoint that is not an object, null for one that is.</param>
internal sealed record DislViewpoint(
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude,
    DislExpression? Members,
    DislCanvas? Canvas,
    DislNotationNames? Notation,
    string? NotAnObject);
