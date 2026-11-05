namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>What the derived-against-hand-written tests ask about in each document.</summary>
internal static class SparqlDerived
{
    /// <summary>An id no document holds.</summary>
    private const string Unknown = "parity:unknown";

    /// <summary>The id prefixes of the elements the wire carries.</summary>
    private static readonly string[] _wirePrefixes = ["var:", "anon:", "iri:", "lit:", "sub:", "region:", "note:", "edge:"];

    /// <summary>Every element <paramref name="projection"/> draws, the header band, the banner and an unknown id.</summary>
    public static IEnumerable<string> Targets(SparqlProjectionResult projection) =>
    [
        .. projection.Regions.Select(region => region.Id),
        .. projection.Nodes.Select(node => node.Id),
        .. projection.Edges.Select(edge => edge.Id),
        .. projection.Annotations.Select(annotation => annotation.Id),
        HandWrittenSparql.HeaderId,
        HandWrittenSparql.TruncationId,
        Unknown,
    ];

    /// <summary>Whether <paramref name="id"/> is one of an element the wire carries, drawn or cut.</summary>
    public static bool IsWireId(string id) => _wirePrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));
}
