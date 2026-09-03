namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>One relationship - the relationships are the diagram (Requirement 5).</summary>
/// <param name="SourceId">The node that declares it.</param>
/// <param name="TargetId">The node it lands on, or empty for an open end - there is no node to point at.</param>
/// <param name="Kind">Which of the five families this is.</param>
/// <param name="Label">What the edge says: a version constraint, a configuring key, an included name; empty when it says nothing.</param>
/// <param name="OpenEnd">Drawn as a marked open end: an Unvendored dependency, or an include no local partial defines - a state, never a crash (R5.2/R5.6).</param>
public sealed record HelmEdge(
    string SourceId,
    string TargetId,
    HelmEdgeKind Kind,
    string Label,
    bool OpenEnd)
{
    /// <summary>
    /// Stable within one graph: keyed by target AND label, because either alone collides -
    /// two includes of different names land on the same partial (same target), and two
    /// declares can share a constraint (same label). The mapper caught the first form as a
    /// duplicate element id, so the composite key is a pinned lesson, not a nicety.
    /// </summary>
    public string Id => $"edge:{SourceId}|{Kind}|{TargetId}|{Label}";
}
