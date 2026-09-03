namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>What kind of node a drawn node is.</summary>
public enum SparqlNodeKind
{
    /// <summary>A named variable - dashed outline, one node per name.</summary>
    Variable,

    /// <summary>An anonymous variable (blank-node syntax) - computed positions only.</summary>
    Anonymous,

    /// <summary>A concrete IRI - solid, prefixed display.</summary>
    Iri,

    /// <summary>A literal - a rectangle with its lexical form.</summary>
    Literal,

    /// <summary>A collapsed subquery - one node stating its projection.</summary>
    SubSelect
}

/// <summary>One drawn node, placed at the shallowest scope that references it.</summary>
/// <param name="Id">The stable element id: <c>var:{name}</c>, <c>anon:{ordinal}</c>, <c>iri:{full}</c>, <c>lit:{escaped}</c>, or <c>sub:{path}.{ordinal}</c>.</param>
/// <param name="Kind">What the node is.</param>
/// <param name="Display">What the node shows: <c>?name</c>, a prefixed name, a lexical form, or a subquery's projection.</param>
/// <param name="ScopePath">The scope whose region frames it - <c>where</c> means the open canvas.</param>
/// <param name="Full">An IRI's full form or a literal's written form; empty for variables.</param>
/// <param name="Annotation">A literal's datatype or language annotation; empty otherwise.</param>
/// <param name="Projected">Whether the projection carries this variable outward.</param>
/// <param name="JoinCount">A variable's triple-pattern degree; 0 for other kinds.</param>
/// <param name="DefiningExpression">The BIND or alias defining a variable, as written; empty otherwise.</param>
public sealed record SparqlNode(
    string Id,
    SparqlNodeKind Kind,
    string Display,
    string ScopePath,
    string Full = "",
    string Annotation = "",
    bool Projected = false,
    int JoinCount = 0,
    string DefiningExpression = "");

/// <summary>One drawn edge: a triple pattern, or a subquery's projected-name join.</summary>
/// <param name="Id">The stable element id, derived from its endpoints, label and ordinal.</param>
/// <param name="FromId">The subject node's id.</param>
/// <param name="ToId">The object node's id.</param>
/// <param name="Label">The predicate or property path, as written.</param>
/// <param name="IsPath">Whether the label is a property path rather than one predicate.</param>
/// <param name="ScopePath">The scope whose group states this pattern - an edge from inside a region may reach a node outside it, and that crossing is the join being shown.</param>
public sealed record SparqlEdge(
    string Id,
    string FromId,
    string ToId,
    string Label,
    bool IsPath,
    string ScopePath);

/// <summary>One containment region: a non-root scope of the tree, or the CONSTRUCT template.</summary>
/// <param name="Id"><c>region:{scope path}</c>.</param>
/// <param name="Kind">The construct kind, lowercase.</param>
/// <param name="Label">The frame's label, term included where the construct has one.</param>
/// <param name="ParentRegionId">The region this one nests inside; empty at the top level.</param>
/// <param name="ScopePath">The scope this region frames.</param>
public sealed record SparqlRegion(
    string Id,
    string Kind,
    string Label,
    string ParentRegionId,
    string ScopePath);

/// <summary>One annotation badge: FILTER, BIND or VALUES text as written, anchored to what it constrains, defines or feeds.</summary>
/// <param name="Id"><c>note:{scope path}/{kind}.{ordinal}</c>.</param>
/// <param name="Kind"><c>filter</c>, <c>bind</c> or <c>values</c>.</param>
/// <param name="Text">The construct exactly as written.</param>
/// <param name="AttachedToId">The element the badge anchors to: a region, a variable node, or empty for the root scope's own filters.</param>
/// <param name="ScopePath">The scope the constraint sits in.</param>
public sealed record SparqlAnnotation(
    string Id,
    string Kind,
    string Text,
    string AttachedToId,
    string ScopePath);

/// <summary>
/// Everything the canvas draws, in deterministic order: same model, same elements, same order.
/// Produced by <see cref="SparqlProjection"/> as a pure function of the model.
/// </summary>
/// <param name="Nodes">The drawn nodes, first-mention order.</param>
/// <param name="Edges">The pattern and subquery-join edges, source order.</param>
/// <param name="Regions">The containment regions, tree order.</param>
/// <param name="Annotations">The constraint badges, tree order.</param>
/// <param name="HeaderForm">The query form line: <c>SELECT DISTINCT</c>, <c>ASK</c>, ...</param>
/// <param name="HeaderRows">The dataset clauses and solution modifiers, as written rows.</param>
/// <param name="Shown">How many elements survived the sanity bound; equals <paramref name="Total"/> when nothing was cut.</param>
/// <param name="Total">How many elements the query would draw unbounded.</param>
public sealed record SparqlProjectionResult(
    IReadOnlyList<SparqlNode> Nodes,
    IReadOnlyList<SparqlEdge> Edges,
    IReadOnlyList<SparqlRegion> Regions,
    IReadOnlyList<SparqlAnnotation> Annotations,
    string HeaderForm,
    IReadOnlyList<string> HeaderRows,
    int Shown,
    int Total)
{
    /// <summary>Whether the sanity bound cut this projection down.</summary>
    public bool Truncated => Shown < Total;
}
