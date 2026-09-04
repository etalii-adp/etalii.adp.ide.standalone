using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One statement of the graph, carrying where in the text it is stated so the writers can splice
/// exactly that and nothing else.
/// </summary>
/// <remarks>
/// The character offsets are the writer's working coordinates and are valid only for the parse
/// they came from: any splice shifts them, so a writer reparses between operations rather than
/// trusting stale positions. <see cref="Span"/> is the same information at line-and-fragment
/// grain, for the tests and for anything that reasons about lines.
/// </remarks>
/// <param name="Subject">The subject: an IRI or a blank node.</param>
/// <param name="Predicate">The predicate, always an IRI.</param>
/// <param name="Object">The object: an IRI, a blank node, or a literal.</param>
/// <param name="Span">Where the triple's own tokens sit - see <see cref="SourceSpan"/> for what "own" means.</param>
/// <param name="Statement">The lines of the whole statement this triple is part of, subject through terminating <c>.</c>.</param>
/// <param name="SpanStart">Offset of the triple's first own token: the predicate for a statement's first pair and each <c>;</c> continuation, the object for a <c>,</c> continuation.</param>
/// <param name="SpanEnd">Offset just past the triple's last own token.</param>
/// <param name="ObjectStart">Offset of the object's first token. Equal to <see cref="SpanStart"/> exactly when the triple is a <c>,</c> continuation.</param>
/// <param name="ObjectEnd">Offset just past the object's last token; equal to <see cref="SpanEnd"/>.</param>
/// <param name="TerminatorOffset">Offset of the statement's terminating <c>.</c>.</param>
public sealed record RdfTriple(
    RdfTerm Subject,
    IriTerm Predicate,
    RdfTerm Object,
    SourceSpan Span,
    LineRange Statement,
    int SpanStart,
    int SpanEnd,
    int ObjectStart,
    int ObjectEnd,
    int TerminatorOffset)
{
    /// <summary>
    /// Whether this triple is a <c>,</c> continuation - its own tokens are the object alone, the
    /// predicate being stated by the list's first triple.
    /// </summary>
    public bool IsListContinuation => SpanStart == ObjectStart;
}
