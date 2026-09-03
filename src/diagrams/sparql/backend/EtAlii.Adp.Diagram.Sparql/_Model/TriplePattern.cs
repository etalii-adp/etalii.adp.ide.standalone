namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One triple pattern of the where clause (or of a <c>CONSTRUCT</c> template): like an RDF
/// triple, but any position may hold a variable - which is exactly what makes it a question.
/// </summary>
/// <param name="Subject">The subject: variable, anonymous, IRI or literal.</param>
/// <param name="Predicate">The predicate: variable, IRI, or a property path as written.</param>
/// <param name="Object">The object: variable, anonymous, IRI or literal.</param>
public sealed record TriplePattern(SparqlTerm Subject, SparqlTerm Predicate, SparqlTerm Object);
