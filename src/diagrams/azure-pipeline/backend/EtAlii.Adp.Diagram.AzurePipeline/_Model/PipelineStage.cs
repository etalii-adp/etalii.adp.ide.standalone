namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// One stage, as declared - or the single implicit one the schema defines around a file that has
/// no <c>stages</c> key (Requirement 4.2).
/// </summary>
/// <param name="Id">Stable within a document: the stage's name, or its position when it has none.</param>
/// <param name="Name">Its <c>stage:</c> value; empty for the implicit stage.</param>
/// <param name="DisplayName">Its <c>displayName</c>, where it has one.</param>
/// <param name="DependsOn">
/// The names in its <c>dependsOn</c>, in order. Empty covers both "no key" and "<c>dependsOn: []</c>";
/// <paramref name="DependsOnDeclared"/> tells them apart, and for a stage the difference decides
/// whether it inherits the sequential default.
/// </param>
/// <param name="DependsOnDeclared">Whether a <c>dependsOn</c> key was present at all.</param>
/// <param name="Gate">
/// The compile-time expression conditionally including this stage, where it sits inside a
/// <c>${{ if ... }}</c> block; empty otherwise. Carried verbatim and never evaluated.
/// </param>
/// <param name="IsImplicit">Whether the schema implied this stage rather than the file declaring it.</param>
/// <param name="Jobs">Its jobs, in declared order.</param>
/// <param name="Lines">The lines declaring it.</param>
public sealed record PipelineStage(
    string Id,
    string Name,
    string DisplayName,
    IReadOnlyList<string> DependsOn,
    bool DependsOnDeclared,
    string Gate,
    bool IsImplicit,
    IReadOnlyList<PipelineJob> Jobs,
    PipelineLineRange Lines)
{
    /// <summary>What to show for this stage.</summary>
    public string Label => DisplayName.Length > 0 ? DisplayName : Name.Length > 0 ? Name : "stage";
}
