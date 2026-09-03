namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One token, addressed by its exact slice of the query text, so error lines can be reported and
/// expression text can be sliced as written - the only two things positions are for here. Nothing
/// ever writes the query back.
/// </summary>
/// <param name="Kind">What the token is.</param>
/// <param name="Start">Offset of the token's first character in the query text.</param>
/// <param name="Length">The token's length in characters.</param>
/// <param name="Value">The token's raw text, exactly as written.</param>
public readonly record struct SparqlToken(SparqlTokenKind Kind, int Start, int Length, string Value)
{
    /// <summary>Offset just past the token's last character.</summary>
    public int End => Start + Length;

    /// <summary>Whether this token is the bare word <paramref name="keyword"/>, compared the case-insensitive way SPARQL keywords are.</summary>
    public bool IsKeyword(string keyword) =>
        Kind == SparqlTokenKind.Name && string.Equals(Value, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this token is the punctuation <paramref name="punctuation"/>.</summary>
    public bool IsPunct(string punctuation) =>
        Kind == SparqlTokenKind.Punct && Value == punctuation;
}
