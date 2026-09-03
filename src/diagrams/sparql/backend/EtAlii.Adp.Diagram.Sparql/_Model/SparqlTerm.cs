namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One position of a triple pattern - the closed set the design names: a variable, an anonymous
/// variable (blank-node syntax, which in a query means a variable), a concrete IRI, a literal,
/// or - in predicate position only - a property path kept as written.
/// </summary>
public abstract record SparqlTerm
{
    /// <summary>The term exactly as the query wrote it, for every surface a human reads.</summary>
    public abstract string AsWritten { get; }
}

/// <summary>A named variable. Every occurrence of one name is the same variable - the join the diagram exists to show.</summary>
/// <param name="Name">The name without its <c>?</c>/<c>$</c> sigil.</param>
/// <param name="Written">The variable as written, sigil included.</param>
public sealed record VariableTerm(string Name, string Written) : SparqlTerm
{
    /// <inheritdoc />
    public override string AsWritten => Written;
}

/// <summary>
/// An anonymous variable: blank-node syntax in a pattern (<c>[]</c>, <c>[ ... ]</c>,
/// <c>_:label</c>, or a collection's cons cells), which SPARQL treats as a variable that cannot
/// be projected. It has no stable identity to key an authored position by, so it always takes a
/// computed place (Requirement 5.4).
/// </summary>
/// <param name="Ordinal">Document-order ordinal, deterministic across reparses of an unchanged file.</param>
/// <param name="Label">The written label for <c>_:label</c> forms; empty for the bracketed forms.</param>
public sealed record AnonymousTerm(int Ordinal, string Label) : SparqlTerm
{
    /// <inheritdoc />
    public override string AsWritten => Label.Length > 0 ? Label : "[]";
}

/// <summary>A concrete IRI, expanded against the prologue and kept as written.</summary>
/// <param name="Iri">The full IRI.</param>
/// <param name="Written">The IRI as written - prefixed, bracketed, or the keyword <c>a</c>.</param>
public sealed record IriTerm(string Iri, string Written) : SparqlTerm
{
    /// <inheritdoc />
    public override string AsWritten => Written;
}

/// <summary>A literal in a pattern, pinning a value rather than asking for one.</summary>
/// <param name="Lexical">The decoded lexical form.</param>
/// <param name="DatatypeIri">The datatype IRI; empty for a plain or language-tagged literal.</param>
/// <param name="Language">The language tag without its <c>@</c>; empty when untagged.</param>
/// <param name="Written">The literal exactly as written, quotes and all.</param>
public sealed record LiteralTerm(string Lexical, string DatatypeIri, string Language, string Written) : SparqlTerm
{
    /// <inheritdoc />
    public override string AsWritten => Written;
}

/// <summary>
/// A property path in predicate position, kept as written - <c>foaf:knows+</c> becomes an edge
/// label, never a parsed structure, per the parser's recognized-but-kept-as-written tier.
/// </summary>
/// <param name="Written">The path exactly as written.</param>
public sealed record PathTerm(string Written) : SparqlTerm
{
    /// <inheritdoc />
    public override string AsWritten => Written;
}
