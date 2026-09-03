namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What one RDF document states: its triples in document order, its prefix declarations, and its
/// base IRI where it has one. Every reading of the family projects over this model; none re-parses.
/// </summary>
/// <param name="Triples">Every triple, in document order - collections and blank property lists expanded to the triples they abbreviate.</param>
/// <param name="Prefixes">Every prefix declaration, in document order, re-declarations included.</param>
/// <param name="BaseIri">The base IRI the document declares, or null.</param>
/// <param name="BaseLine">The line index of the base declaration, zero-based; -1 when there is none.</param>
public sealed record RdfModel(
    IReadOnlyList<RdfTriple> Triples,
    IReadOnlyList<PrefixDeclaration> Prefixes,
    string? BaseIri,
    int BaseLine)
{
    /// <summary>The model of a document that states nothing, or one that could not be parsed.</summary>
    public static readonly RdfModel Empty = new([], [], null, -1);

    /// <summary>
    /// The winning expansion for <paramref name="prefix"/> - the last declaration, matching how
    /// Turtle scopes re-declarations - or null where the prefix is not declared at all.
    /// </summary>
    public string? Expansion(string prefix)
    {
        for (var i = Prefixes.Count - 1; i >= 0; i--)
        {
            if (Prefixes[i].Prefix == prefix)
            {
                return Prefixes[i].Iri;
            }
        }

        return null;
    }
}
