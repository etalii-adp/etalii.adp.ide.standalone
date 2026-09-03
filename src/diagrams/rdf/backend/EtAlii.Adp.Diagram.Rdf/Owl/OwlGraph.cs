namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>What kind of node an ontology element draws as (owl-diagram Requirement 1).</summary>
public enum OwlNodeKind
{
    /// <summary>A named class - a circle in the adopted VOWL vocabulary.</summary>
    Class,

    /// <summary>A datatype - a rectangle; the schema half of the literal-node position.</summary>
    Datatype,

    /// <summary>A named individual - a card in the family shape; the data half of that position.</summary>
    Individual,

    /// <summary>An <c>owl:Thing</c> anchor - stated, or materialized for a domain-less or range-less property.</summary>
    Thing,

    /// <summary>The <c>owl:Ontology</c> header element.</summary>
    OntologyHeader,

    /// <summary>A restriction expression - blank-node-rooted, drawn beside its owner (Requirement 3).</summary>
    Restriction,

    /// <summary>A set-operator expression (union, intersection, complement, enumeration) - equally blank-node-rooted.</summary>
    Operator,
}

/// <summary>What kind of axiom an edge draws (owl-diagram Requirement 2).</summary>
public enum OwlEdgeKind
{
    /// <summary>An asserted <c>rdfs:subClassOf</c> - the dotted VOWL edge.</summary>
    Subclass,

    /// <summary>An asserted <c>owl:equivalentClass</c>.</summary>
    Equivalent,

    /// <summary>An asserted <c>owl:disjointWith</c>, or one pair of an <c>owl:AllDisjointClasses</c> group.</summary>
    Disjoint,

    /// <summary>An object property between its stated domain and range (or their Thing anchors).</summary>
    ObjectProperty,

    /// <summary>A datatype property from its domain to its range's datatype node.</summary>
    DatatypeProperty,

    /// <summary>An object-property assertion between two individuals.</summary>
    Assertion,

    /// <summary>The wiring of an expression: owner-to-expression carries the axiom, expression-to-referenced-term carries the structure.</summary>
    Expression,
}

/// <summary>
/// One drawn element of the ontology reading.
/// </summary>
/// <remarks>
/// Named terms carry family <c>res:{iri}</c> ids (a punned individual role carries <c>ind:{iri}</c>
/// so both roles of one IRI can draw, Requirement 2.4 - still IRI-derived, so still overlay-safe).
/// Expression nodes carry structural <c>expr:</c> ids, deterministic within a parse and deliberately
/// unstable across edits - the blank-node identity boundary's reason, kept true by a test.
/// </remarks>
/// <param name="Id">The element id, per the shapes above.</param>
/// <param name="Kind">What the node draws as.</param>
/// <param name="Iri">The full IRI; empty for expression nodes and materialized anchors.</param>
/// <param name="Display">The label: <c>rdfs:label</c> preferred, prefixed name as fallback (Requirement 1.4). Expression labels arrive from the renderer.</param>
/// <param name="Badges">Type badges on an individual card; empty elsewhere.</param>
/// <param name="Rows">Annotation and literal-assertion rows, the family card convention.</param>
/// <param name="Deprecated">Whether <c>owl:deprecated true</c> is asserted - drawn dimmed (Requirement 1.6).</param>
/// <param name="External">Whether the IRI is used here but declared nowhere in this file - drawn dimmed (Requirement 1.6).</param>
/// <param name="OwnerId">For an expression node, the element id of the named class whose axiom attaches it; empty elsewhere.</param>
/// <param name="ExpressionRoot">For an expression node, the blank term rooting its structure - what the renderer walks; null elsewhere.</param>
/// <param name="Malformed">Whether the expression structure is broken (Requirement 3.5) - drawn as a marked problem node.</param>
public sealed record OwlNode(
    string Id,
    OwlNodeKind Kind,
    string Iri,
    string Display,
    IReadOnlyList<string> Badges,
    IReadOnlyList<RdfProjectionRow> Rows,
    bool Deprecated,
    bool External,
    string OwnerId = "",
    RdfTerm? ExpressionRoot = null,
    bool Malformed = false);

/// <summary>One drawn axiom or assertion (owl-diagram Requirement 2).</summary>
/// <param name="Id">The element id, unique within the projection.</param>
/// <param name="Kind">Which axiom the edge states - what the canvas styles by.</param>
/// <param name="FromId">The source node's element id.</param>
/// <param name="ToId">The target node's element id.</param>
/// <param name="Label">The edge label: a property's display with its characteristic words folded in; empty for pure axiom edges.</param>
/// <param name="PropertyIri">The property's full IRI for property and assertion edges; the axiom predicate's IRI otherwise.</param>
public sealed record OwlEdge(
    string Id,
    OwlEdgeKind Kind,
    string FromId,
    string ToId,
    string Label,
    string PropertyIri);

/// <summary>
/// What the ontology reading draws: nodes and edges up to the drawn-element budget, the cut
/// keeping each class's expression neighborhood whole (Requirement 8.3).
/// </summary>
/// <param name="Nodes">The drawn elements, units in document order of first appearance.</param>
/// <param name="Edges">The drawn edges - only those whose both endpoints made the cut.</param>
/// <param name="Shown">How many nodes are drawn.</param>
/// <param name="Total">How many the ontology projects to before the cut.</param>
public sealed record OwlGraphResult(
    IReadOnlyList<OwlNode> Nodes,
    IReadOnlyList<OwlEdge> Edges,
    int Shown,
    int Total)
{
    /// <summary>Whether the budget cut the graph down - the state that shows the banner and withholds edits.</summary>
    public bool Truncated => Shown < Total;
}
