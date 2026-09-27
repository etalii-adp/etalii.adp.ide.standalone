namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One relationship, resolved: which node declared it, what it points at, and - through
/// <paramref name="Directive"/> - the file and line that wrote it, the target as written and any
/// <c>when:</c>. That last part is what makes "why is this here" answerable from the property
/// grid (Requirement 10.6).
/// </summary>
/// <param name="SourceId">The node that declared it.</param>
/// <param name="TargetId">
/// The node it points at, or empty when <paramref name="Resolution"/> is not
/// <see cref="AnsibleTargetResolution.Resolved"/> - there is no node to point at.
/// </param>
/// <param name="Kind">Which of the five relationships this is.</param>
/// <param name="Resolution">Whether the target was found, is absent, or is unknowable.</param>
/// <param name="Directive">The declaration itself, exactly as the file wrote it.</param>
public sealed record AnsibleEdge(
    string SourceId,
    string TargetId,
    AnsibleEdgeKind Kind,
    AnsibleTargetResolution Resolution,
    AnsibleDirective Directive)
{
    /// <summary>
    /// Stable within one graph. The target as written rather than the resolved id, so a missing
    /// and an unresolvable edge both have an identity - and so an edge keeps the same id when
    /// the role it names finally appears.
    /// </summary>
    /// <remarks>
    /// <b>A <see cref="AnsibleEdgeKind.Targets"/> edge adds the inventory it reaches.</b> One
    /// <c>hosts:</c> pattern yields one edge per inventory that defines it, so the pattern alone
    /// named two edges with one id and the canvas drew only one of them. Such an edge is only
    /// ever made resolved - a pattern nothing defines yields no edge - so the inventory is always
    /// there to add, and the reason the other kinds leave the resolved id out does not apply.
    /// </remarks>
    public string Id => Kind == AnsibleEdgeKind.Targets
        ? $"edge:{SourceId}|{Kind}|{Directive.Target}|{TargetId}"
        : $"edge:{SourceId}|{Kind}|{Directive.Target}";

    /// <summary>Whether Ansible resolves this during the run rather than before it - drawn dashed (Requirement 5.4).</summary>
    public bool IsDynamic => Directive.IsDynamic;
}
