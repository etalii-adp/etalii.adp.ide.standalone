namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>One literal property, rendered as a row inside its subject's card rather than as a node (Requirement 3.2).</summary>
/// <param name="Predicate">The predicate, display form.</param>
/// <param name="Value">The literal's lexical form.</param>
/// <param name="Annotation">A language tag with its <c>@</c>, a compressed datatype, or empty for a plain string.</param>
public sealed record RdfProjectionRow(string Predicate, string Value, string Annotation);
