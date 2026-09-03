namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One prefix declaration, at the line that states it. Every declaration is kept, re-declarations
/// included, so the validator can point at a duplicate rather than silently keeping one of them.
/// </summary>
/// <param name="Prefix">The declared prefix, without its colon; empty for the default prefix.</param>
/// <param name="Iri">The namespace IRI it expands to.</param>
/// <param name="Line">The line index of the declaration, zero-based.</param>
public sealed record PrefixDeclaration(string Prefix, string Iri, int Line);
