namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What the scheme reading draws, cut to the drawn-element budget in the hierarchy-aware
/// deterministic order (skos-diagram Requirement 8.2): schemes by IRI, then per scheme its top
/// concepts and their descendants breadth-first by layer with IRI ties, then unfiled concepts by
/// IRI, then collections by IRI.
/// </summary>
/// <param name="Schemes">The drawn schemes, in the budget order.</param>
/// <param name="Concepts">The drawn concepts, in the budget order.</param>
/// <param name="Collections">The drawn collections, in the budget order.</param>
/// <param name="Edges">The drawn edges - only those whose both endpoints made the cut.</param>
/// <param name="OutOfFileMappings">Mapping triples whose far end is not an in-file concept - property-grid rows, never stub nodes (Requirement 1.3).</param>
/// <param name="Shown">How many elements (schemes + concepts + collections) are drawn.</param>
/// <param name="Total">How many the document states.</param>
public sealed record SkosProjectionResult(
    IReadOnlyList<SkosScheme> Schemes,
    IReadOnlyList<SkosConcept> Concepts,
    IReadOnlyList<SkosCollection> Collections,
    IReadOnlyList<SkosEdge> Edges,
    IReadOnlyList<RdfTriple> OutOfFileMappings,
    int Shown,
    int Total)
{
    /// <summary>Whether the budget cut the vocabulary down - the state that shows the banner and withholds edits.</summary>
    public bool Truncated => Shown < Total;
}
