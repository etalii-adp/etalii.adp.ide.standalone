using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One cycle in the asserted hierarchy: the concepts on it, and the edge the layering excluded
/// to break it. The validator consumes this rather than detecting again, so the picture and the
/// problem report cannot disagree (skos-diagram Requirements 4.2, 7.1).
/// </summary>
/// <param name="ConceptIds">The element ids on the cycle, sorted ordinally.</param>
/// <param name="ExcludedEdgeId">The excluded edge - the one whose (broader IRI, narrower IRI) pair sorts lowest; still drawn.</param>
/// <param name="Triples">Every triple stating an edge on the cycle, for the finding's lines.</param>
public sealed record SkosCycle(
    IReadOnlyList<string> ConceptIds,
    string ExcludedEdgeId,
    IReadOnlyList<RdfTriple> Triples);

/// <summary>The computed layout, and what its cycle detection found on the way.</summary>
/// <param name="Positions">A position for every drawn element, ids as the mapper speaks them.</param>
/// <param name="Cycles">Every hierarchy cycle, one entry per strongly connected knot.</param>
public sealed record SkosLayoutResult(
    IReadOnlyDictionary<string, RegistrationPosition> Positions,
    IReadOnlyList<SkosCycle> Cycles);
