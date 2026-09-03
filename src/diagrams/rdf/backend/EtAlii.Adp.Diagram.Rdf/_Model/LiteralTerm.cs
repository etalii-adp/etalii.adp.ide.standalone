namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>A literal value.</summary>
/// <param name="Lexical">The value with string escapes decoded.</param>
/// <param name="DatatypeIri">The full datatype IRI; null for a plain string, which RDF defines as <c>xsd:string</c> by omission.</param>
/// <param name="Language">The language tag of a <c>@lang</c> literal, without its <c>@</c>; null otherwise.</param>
/// <param name="AsWritten">The exact spelling in the document, quotes, escapes and annotations included.</param>
public sealed record LiteralTerm(string Lexical, string? DatatypeIri, string? Language, string AsWritten) : RdfTerm;
