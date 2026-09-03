namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// One constraint row on a card: a property shape rendered as a line rather than as the
/// blank-node constellation it is written as - the projection that dissolves the identity
/// boundary, because a row has no position at all (shacl-diagram Requirement 3.1).
/// </summary>
/// <param name="Path">The <c>sh:path</c> printed as SHACL path syntax; empty for a card-level row (a SPARQL constraint, a combinator summary).</param>
/// <param name="Name">The property shape's <c>sh:name</c>, or empty.</param>
/// <param name="Summary">The constraint summary, fixed parameter order.</param>
/// <param name="Cardinality">The <c>[min..max]</c> form, <c>*</c> where no maximum is stated; empty for card-level rows.</param>
/// <param name="Sparql">Whether this is an opaque SPARQL row - drawn, never parsed beyond extraction, never executed.</param>
/// <param name="Blank">Whether the row's shape is blank-rooted - readable everywhere, editable nowhere (Requirement 3.3).</param>
/// <param name="Severity">The row's own non-default severity display, or empty.</param>
public sealed record ShaclRow(
    string Path,
    string Name,
    string Summary,
    string Cardinality,
    bool Sparql,
    bool Blank,
    string Severity);
