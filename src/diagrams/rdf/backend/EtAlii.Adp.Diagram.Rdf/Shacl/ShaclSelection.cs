namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The shapes reading's addition to the family selection vocabulary, beside <see cref="OwlSelection"/>:
/// <c>shacl-edge:{fromId}|{kind}|{toId}</c>. A SHACL edge - a <c>sh:node</c>, a combinator, a
/// <c>sh:class</c> claim - is derived by the projection rather than stated by one triple, so
/// <see cref="RdfSelection"/> alone cannot name it, and until this existed no resolver could: every edge a
/// shapes canvas drew was refused, so none could be selected (centralized-selection task 26).
/// </summary>
internal static class ShaclSelection
{
    /// <summary>The id prefix every drawn shapes edge carries.</summary>
    private const string EdgePrefix = "shacl-edge:";

    /// <summary>
    /// The drawn shapes edge an id names, as "from → to", or null - the projection is the authority,
    /// because the edge exists only there. Projected unbudgeted, as the session lays the diagram out.
    /// </summary>
    public static string? Describe(RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith(EdgePrefix, StringComparison.Ordinal) || !entry.IsUsable)
        {
            return null;
        }

        var projection = ShaclProjection.Project(entry.Model, int.MaxValue);
        return projection.Edges.FirstOrDefault(edge => edge.Id == elementId) is not { } found
            ? null
            : $"{DisplayOf(projection, found.FromId)} → {DisplayOf(projection, found.ToId)}";
    }

    private static string DisplayOf(ShaclProjectionResult projection, string cardId) =>
        projection.Cards.FirstOrDefault(card => card.Id == cardId)?.Display ?? cardId;
}
