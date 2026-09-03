namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>The kinds of token Turtle and N-Triples are made of, as the tokenizer classifies them.</summary>
public enum RdfTokenKind
{
    /// <summary>A bracketed IRI, <c>&lt;...&gt;</c>.</summary>
    Iri,

    /// <summary>A prefixed name, <c>ex:thing</c> or the bare default-prefix form <c>:thing</c>.</summary>
    PrefixedName,

    /// <summary>A labeled blank node, <c>_:name</c>.</summary>
    BlankNodeLabel,

    /// <summary>A quoted string in any of Turtle's four quoting styles.</summary>
    String,

    /// <summary>A language tag, <c>@en</c>.</summary>
    LanguageTag,

    /// <summary>A bare numeric literal.</summary>
    Number,

    /// <summary>The keyword <c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>The keyword <c>a</c>, which in verb position means <c>rdf:type</c>.</summary>
    A,

    /// <summary>The <c>@prefix</c> directive.</summary>
    PrefixDirective,

    /// <summary>The <c>@base</c> directive.</summary>
    BaseDirective,

    /// <summary>The SPARQL-style <c>PREFIX</c> directive, which takes no terminating dot.</summary>
    SparqlPrefix,

    /// <summary>The SPARQL-style <c>BASE</c> directive, which takes no terminating dot.</summary>
    SparqlBase,

    /// <summary>The statement terminator, <c>.</c>.</summary>
    Dot,

    /// <summary>The predicate-list separator, <c>;</c>.</summary>
    Semicolon,

    /// <summary>The object-list separator, <c>,</c>.</summary>
    Comma,

    /// <summary>The datatype marker, <c>^^</c>.</summary>
    DoubleCaret,

    /// <summary>The start of a collection, <c>(</c>.</summary>
    OpenParen,

    /// <summary>The end of a collection, <c>)</c>.</summary>
    CloseParen,

    /// <summary>The start of a blank node property list, <c>[</c>.</summary>
    OpenBracket,

    /// <summary>The end of a blank node property list, <c>]</c>.</summary>
    CloseBracket,

    /// <summary>The end of the document.</summary>
    EndOfFile
}
