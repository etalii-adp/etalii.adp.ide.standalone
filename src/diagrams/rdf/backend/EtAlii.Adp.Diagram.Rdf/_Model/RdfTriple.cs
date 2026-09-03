namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One statement of the graph, carrying where in the text it is stated so the writers can splice
/// exactly that and nothing else.
/// </summary>
/// <param name="Subject">The subject: an IRI or a blank node.</param>
/// <param name="Predicate">The predicate, always an IRI.</param>
/// <param name="Object">The object: an IRI, a blank node, or a literal.</param>
/// <param name="Span">Where the triple's own tokens sit - see <see cref="SourceSpan"/> for what "own" means.</param>
/// <param name="Statement">The lines of the whole statement this triple is part of, subject through terminating <c>.</c>.</param>
public sealed record RdfTriple(RdfTerm Subject, IriTerm Predicate, RdfTerm Object, SourceSpan Span, LineRange Statement);
