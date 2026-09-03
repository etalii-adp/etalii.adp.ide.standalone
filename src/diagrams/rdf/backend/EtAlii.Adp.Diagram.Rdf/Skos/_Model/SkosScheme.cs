namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>One concept scheme, drawn as a titled region (skos-diagram Requirement 1.1).</summary>
/// <param name="Id">The element id, <c>res:{iri}</c>.</param>
/// <param name="Iri">The full IRI.</param>
/// <param name="Labels">Every label the file states for it, in document order.</param>
/// <param name="TopConceptIds">The asserted top concepts (<c>topConceptOf</c> or <c>hasTopConcept</c>, either direction), in document order.</param>
public sealed record SkosScheme(
    string Id,
    string Iri,
    IReadOnlyList<SkosLabel> Labels,
    IReadOnlyList<string> TopConceptIds);
