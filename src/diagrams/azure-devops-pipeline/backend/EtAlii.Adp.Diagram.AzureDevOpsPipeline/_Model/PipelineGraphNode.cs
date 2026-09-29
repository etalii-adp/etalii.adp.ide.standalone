namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The part of a stage or job the dependency rules actually use.
/// </summary>
/// <remarks>
/// The rules are the same shape at both levels and differ only in their default, so they are
/// written once against this rather than twice against <see cref="PipelineStage"/> and
/// <see cref="PipelineJob"/> - where the second copy would be the one that drifted.
/// </remarks>
/// <param name="Id">Its id, which is what an edge points at.</param>
/// <param name="Name">Its name, which is what a <c>dependsOn</c> names.</param>
/// <param name="DependsOn">The names it declared, in order.</param>
/// <param name="DependsOnDeclared">Whether it declared the key at all.</param>
/// <param name="Condition">Its <c>condition</c>, verbatim.</param>
public sealed record PipelineGraphNode(
    string Id,
    string Name,
    IReadOnlyList<string> DependsOn,
    bool DependsOnDeclared,
    string Condition);
