namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One collection, drawn as a labeled group of its members - never conflated with the hierarchy,
/// per SKOS S13's disjoint families (skos-diagram Requirement 1.5).
/// </summary>
/// <param name="Id">The element id, <c>res:{iri}</c>.</param>
/// <param name="Iri">The full IRI.</param>
/// <param name="Labels">Every label the file states for it, in document order.</param>
/// <param name="Ordered">Whether this is a <c>skos:OrderedCollection</c>.</param>
/// <param name="MemberIds">The member element ids - <c>skos:member</c> in document order, or the <c>memberList</c>'s own order for the ordered kind.</param>
public sealed record SkosCollection(
    string Id,
    string Iri,
    IReadOnlyList<SkosLabel> Labels,
    bool Ordered,
    IReadOnlyList<string> MemberIds);
