namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Hands out the family's edge ids, <c>edge:{fromId}|{predicateIri}|{toId}</c>, adding <c>|n</c> for the n-th repeat of one
/// such triple, so the data-graph and OWL readings name a drawn edge the same way and the client can address it.
/// </summary>
internal sealed class RdfEdgeIds
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>The id of the next edge from <paramref name="fromId"/> to <paramref name="toId"/> through <paramref name="predicateIri"/>.</summary>
    public string Next(string fromId, string predicateIri, string toId)
    {
        var key = $"{fromId}|{predicateIri}|{toId}";
        _counts.TryGetValue(key, out var count);
        _counts[key] = count + 1;
        return count == 0 ? $"edge:{key}" : $"edge:{key}|{count}";
    }
}
