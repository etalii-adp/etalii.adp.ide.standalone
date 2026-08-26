namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// One "waits for": <paramref name="ToId"/> does not start until <paramref name="FromId"/> is
/// done.
/// </summary>
/// <param name="FromId">What is waited for.</param>
/// <param name="ToId">What waits.</param>
/// <param name="FromName">The name as written, which is all there is when the edge is broken.</param>
/// <param name="Condition">Which outcome lets the dependent run.</param>
/// <param name="ConditionText">The <c>condition</c> verbatim, empty where there was none.</param>
/// <param name="IsImplicit">
/// Whether the file said so. A stage with no <c>dependsOn</c> waits for the stage declared before
/// it, which is the single most easily missed thing about an Azure pipeline - the diagram draws
/// the arrow, and this is what lets it draw it differently from one somebody wrote down.
/// </param>
/// <param name="IsBroken">
/// Whether <paramref name="FromName"/> names nothing that exists. The edge is kept rather than
/// dropped: a dangling dependency is precisely the mistake this diagram should catch
/// (Requirement 6.5).
/// </param>
public sealed record PipelineEdge(
    string FromId,
    string ToId,
    string FromName,
    PipelineEdgeCondition Condition,
    string ConditionText,
    bool IsImplicit,
    bool IsBroken)
{
    /// <summary>Whether a <c>condition</c> was declared on the dependent, whatever it says.</summary>
    public bool IsConditional => ConditionText.Length > 0;
}
