namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>One drawn edge - a triple between two drawn terms (Requirement 3.1).</summary>
/// <param name="Id">The element id, unique within the projection.</param>
/// <param name="FromId">The subject node's element id.</param>
/// <param name="ToId">The object node's element id.</param>
/// <param name="Predicate">The predicate, display form - the edge's label.</param>
/// <param name="PredicateIri">The predicate's full IRI, which edits key off.</param>
public sealed record RdfProjectionEdge(string Id, string FromId, string ToId, string Predicate, string PredicateIri);
