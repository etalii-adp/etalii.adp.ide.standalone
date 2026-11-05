namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One triple pattern of the where clause (or of a <c>CONSTRUCT</c> template): like an RDF
/// triple, but any position may hold a variable - which is exactly what makes it a question.
/// </summary>
/// <param name="Subject">The subject: variable, anonymous, IRI or literal.</param>
/// <param name="Predicate">The predicate: variable, IRI, or a property path as written.</param>
/// <param name="Object">The object: variable, anonymous, IRI or literal.</param>
public sealed record TriplePattern(SparqlTerm Subject, SparqlTerm Predicate, SparqlTerm Object)
{
    /// <summary>
    /// Where the pattern is written: from the first character of its subject to the last of its
    /// object. A pattern of a <c>;</c> or <c>,</c> list starts at the subject it shares, and one a
    /// collection's cells give starts at its cell.
    /// </summary>
    public SparqlSpan Span { get; init; }
}
