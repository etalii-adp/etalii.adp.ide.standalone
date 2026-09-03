namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// A triple the parser has read but not yet placed: its terms plus the character offsets of its
/// own tokens, held until the statement's terminating dot fixes the statement range and the
/// offsets can be turned into a <see cref="SourceSpan"/>.
/// </summary>
/// <param name="Subject">The subject term.</param>
/// <param name="Predicate">The predicate term.</param>
/// <param name="Object">The object term.</param>
/// <param name="Start">Offset of the triple's first own token.</param>
/// <param name="End">Offset just past the triple's last own token.</param>
/// <param name="ObjectStart">Offset of the object's first token.</param>
internal sealed record RdfPendingTriple(RdfTerm Subject, IriTerm Predicate, RdfTerm Object, int Start, int End, int ObjectStart);
