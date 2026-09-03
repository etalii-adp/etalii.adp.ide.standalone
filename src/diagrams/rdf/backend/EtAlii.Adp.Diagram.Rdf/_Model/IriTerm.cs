namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>An IRI-named term.</summary>
/// <remarks>
/// The full IRI is the term's identity - two spellings of one IRI (prefixed, absolute, the
/// <c>a</c> keyword) are the same term. The as-written form is kept beside it because the splice
/// discipline forbids normalising what an edit does not touch: a writer that has to restate a
/// term restates it the way the author wrote it.
/// </remarks>
/// <param name="Iri">The full IRI, prefixes expanded and any base applied. A relative IRI in a base-less document stays relative rather than being guessed at.</param>
/// <param name="AsWritten">The exact spelling in the document: a prefixed name, a bracketed IRI, or the keyword <c>a</c>.</param>
public sealed record IriTerm(string Iri, string AsWritten) : RdfTerm;
