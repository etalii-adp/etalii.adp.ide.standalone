namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>One drawn resource of the data graph - a card (Requirement 3.1).</summary>
/// <param name="Id">The element id: <c>res:{iri}</c>, or <c>blank:{ordinal}</c> for a blank node.</param>
/// <param name="Iri">The full IRI; empty for a blank node.</param>
/// <param name="Display">The card's title: a prefixed name, the IRI's local name, or a <c>_:label</c>.</param>
/// <param name="Types">The <c>rdf:type</c> badges, display form, in document order (Requirement 3.3).</param>
/// <param name="Rows">The literal property rows (Requirement 3.2).</param>
/// <param name="Blank">Whether this is a blank node - drawn, but position- and edit-refused per the identity boundary.</param>
public sealed record RdfProjectionNode(
    string Id,
    string Iri,
    string Display,
    IReadOnlyList<string> Types,
    IReadOnlyList<RdfProjectionRow> Rows,
    bool Blank);
