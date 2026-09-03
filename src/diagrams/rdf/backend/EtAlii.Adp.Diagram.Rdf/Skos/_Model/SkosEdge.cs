namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>The three edge languages of the scheme reading (skos-diagram Requirements 1.2, 1.3).</summary>
public enum SkosEdgeKind
{
    /// <summary>Broader/narrower, drawn solid, broader above narrower.</summary>
    Hierarchy,

    /// <summary>skos:related, drawn dashed, never affecting the layering.</summary>
    Related,

    /// <summary>A mapping property between two in-file concepts, drawn dotted.</summary>
    Mapping,
}

/// <summary>
/// One drawn edge. A hierarchy edge exists where <c>skos:broader</c> or <c>skos:narrower</c> is
/// asserted in either direction - a projection of asserted triples, one edge per pair however
/// many directions the file states, every stating triple carried so a disconnect removes them
/// all and validation can name every line. Nothing is completed and nothing is written back.
/// </summary>
/// <param name="Id">The element id, in the family's <c>edge:{fromId}|{predicateIri}|{toId}</c> shape, canonicalised per kind so one pair yields one id.</param>
/// <param name="FromId">Hierarchy: the broader concept (drawn above). Related: the smaller id. Mapping: the asserting subject.</param>
/// <param name="ToId">Hierarchy: the narrower concept. Related: the larger id. Mapping: the asserted object.</param>
/// <param name="Kind">Which edge language this is.</param>
/// <param name="PredicateIri">The predicate the id carries - <c>skos:broader</c> for every hierarchy edge, <c>skos:related</c>, or the mapping property.</param>
/// <param name="Triples">Every asserted triple this edge projects, in document order.</param>
/// <param name="AssertedBothWays">Whether the file asserts both directions of a hierarchy or related pair.</param>
public sealed record SkosEdge(
    string Id,
    string FromId,
    string ToId,
    SkosEdgeKind Kind,
    string PredicateIri,
    IReadOnlyList<RdfTriple> Triples,
    bool AssertedBothWays);
