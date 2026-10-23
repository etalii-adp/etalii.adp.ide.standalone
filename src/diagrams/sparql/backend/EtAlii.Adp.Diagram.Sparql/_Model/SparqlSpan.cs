namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Where an entry of a query was read from: the characters it was written as and the line it starts
/// on. What a persistence plugin delivers for every element it reads (FBL 11.2), so the parser keeps
/// it on every group, pattern, constraint, subquery and prefix declaration (sparql-disl-fbl, finding B3).
/// </summary>
/// <param name="Start">The offset of its first character.</param>
/// <param name="End">The offset just past its last character.</param>
/// <param name="Line">The one-based line it starts on; 0 for an entry with no text of its own.</param>
public readonly record struct SparqlSpan(int Start, int End, int Line)
{
    /// <summary>The text it covers in <paramref name="text"/>.</summary>
    public string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text[Start..End];
    }
}
