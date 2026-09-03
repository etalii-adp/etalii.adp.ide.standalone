namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// What a new property row states beyond its path: the handful of constraints the add-row dialog
/// offers, each omitted from the written block when it is null (shacl-diagram Requirement 5.4).
/// </summary>
/// <param name="DatatypeIri">The full datatype IRI for a <c>sh:datatype</c> constraint, or null.</param>
/// <param name="MinCount">The <c>sh:minCount</c>, or null.</param>
/// <param name="MaxCount">The <c>sh:maxCount</c>, or null.</param>
/// <param name="Name">The <c>sh:name</c>, or null.</param>
public sealed record ShaclPropertyShapeOptions(
    string? DatatypeIri = null,
    int? MinCount = null,
    int? MaxCount = null,
    string? Name = null);
