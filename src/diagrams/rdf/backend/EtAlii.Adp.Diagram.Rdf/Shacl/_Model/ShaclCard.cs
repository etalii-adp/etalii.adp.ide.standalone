namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>One drawn shape - a card (shacl-diagram Requirement 1.1).</summary>
/// <param name="Id">The element id: <c>res:{iri}</c>, or <c>blank:{ordinal}</c> for an anonymous shape.</param>
/// <param name="Iri">The full IRI; empty for an anonymous shape.</param>
/// <param name="Display">The card's title: prefixed name, local name, or an anonymous marker.</param>
/// <param name="Blank">The identity-boundary marker: anonymous cards draw at computed positions only.</param>
/// <param name="Deactivated">Whether the shape carries <c>sh:deactivated true</c> - dimmed, badged.</param>
/// <param name="Severity">The non-default severity's display form, or empty.</param>
/// <param name="Closed">Whether the shape declares <c>sh:closed true</c>.</param>
/// <param name="Name">The shape's <c>sh:name</c>, or empty.</param>
/// <param name="Description">The shape's <c>sh:description</c>, or empty.</param>
/// <param name="Targets">The target declarations, as chips - card content, never elements (Requirement 1.3).</param>
/// <param name="Rows">The constraint rows, in source order.</param>
public sealed record ShaclCard(
    string Id,
    string Iri,
    string Display,
    bool Blank,
    bool Deactivated,
    string Severity,
    bool Closed,
    string Name,
    string Description,
    IReadOnlyList<ShaclTargetChip> Targets,
    IReadOnlyList<ShaclRow> Rows);
