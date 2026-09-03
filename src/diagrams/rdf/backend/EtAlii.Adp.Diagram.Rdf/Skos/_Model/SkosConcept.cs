namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>One drawn concept (skos-diagram Requirement 1.1).</summary>
/// <param name="Id">The element id: <c>res:{iri}</c>, or <c>blank:{ordinal}</c> for a blank-node concept.</param>
/// <param name="Iri">The full IRI; empty for a blank node.</param>
/// <param name="Labels">Every label the file states for it, in document order.</param>
/// <param name="Notations">Every <c>skos:notation</c> lexical, in document order; the canvas badges the first.</param>
/// <param name="SchemeIris">The schemes it is asserted into, in document order; empty means the unfiled band.</param>
/// <param name="Blank">Whether this is a blank node - drawn, position- and edit-refused per the identity boundary.</param>
public sealed record SkosConcept(
    string Id,
    string Iri,
    IReadOnlyList<SkosLabel> Labels,
    IReadOnlyList<string> Notations,
    IReadOnlyList<string> SchemeIris,
    bool Blank);
