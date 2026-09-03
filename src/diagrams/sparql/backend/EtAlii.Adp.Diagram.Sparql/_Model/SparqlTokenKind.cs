namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>The kinds of token a SPARQL query is made of, as the tokenizer classifies them.</summary>
public enum SparqlTokenKind
{
    /// <summary>A bracketed IRI, <c>&lt;...&gt;</c>.</summary>
    Iri,

    /// <summary>A prefixed name, <c>foaf:name</c> or the bare default-prefix form <c>:thing</c>.</summary>
    PrefixedName,

    /// <summary>A variable, <c>?name</c> or <c>$name</c>.</summary>
    Variable,

    /// <summary>A labeled blank node, <c>_:name</c> - in a query, a variable in disguise.</summary>
    BlankNodeLabel,

    /// <summary>A quoted string in any of the four quoting styles.</summary>
    String,

    /// <summary>A language tag, <c>@en</c>.</summary>
    LanguageTag,

    /// <summary>A bare numeric literal.</summary>
    Number,

    /// <summary>The keyword <c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>
    /// A bare word: a SPARQL keyword (<c>SELECT</c>, <c>OPTIONAL</c>), the verb <c>a</c>, or a
    /// built-in call's name inside an expression (<c>REGEX</c>). The parser decides which, so a
    /// word the grammar reserves can still appear as written inside a sliced expression.
    /// </summary>
    Name,

    /// <summary>
    /// Punctuation and operators, the exact characters in <see cref="SparqlToken.Value"/>:
    /// braces, brackets, parens, separators, path operators and expression operators alike. The
    /// parser switches on the value; inside expressions the value is never interpreted at all,
    /// only carried as written.
    /// </summary>
    Punct,

    /// <summary>The end of the document.</summary>
    EndOfFile
}
