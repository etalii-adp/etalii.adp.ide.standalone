namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// One job, as declared - or the single implicit one the schema defines around a file that has
/// only a <c>steps</c> key (Requirement 4.2).
/// </summary>
/// <param name="Id">Stable within a document: the owning stage's id and this job's name or position.</param>
/// <param name="Name">Its <c>job:</c> or <c>deployment:</c> value; empty for the implicit job.</param>
/// <param name="DisplayName">Its <c>displayName</c>, where it has one.</param>
/// <param name="IsDeployment">Whether it was declared as <c>deployment:</c> rather than <c>job:</c>.</param>
/// <param name="Environment">A deployment job's <c>environment</c>; empty otherwise.</param>
/// <param name="Strategy">Its <c>strategy</c> block, or <see cref="PipelineStrategy.None"/>.</param>
/// <param name="Pool">Where it runs, after the schema's inheritance has been applied (Requirement 4.7).</param>
/// <param name="Execution">What decides whether and how it runs (Requirement 4.5).</param>
/// <param name="DependsOn">
/// The names in its <c>dependsOn</c>, in order. Empty covers both "no <c>dependsOn</c> key" and
/// "<c>dependsOn: []</c>", which mean different things - <paramref name="DependsOnDeclared"/> is
/// what tells them apart, and the graph is what acts on the difference.
/// </param>
/// <param name="DependsOnDeclared">Whether a <c>dependsOn</c> key was present at all.</param>
/// <param name="Gate">
/// The compile-time expression conditionally including this job, where it sits inside a
/// <c>${{ if ... }}</c> block; empty otherwise. Carried verbatim and never evaluated.
/// </param>
/// <param name="IsImplicit">Whether the schema implied this job rather than the file declaring it.</param>
/// <param name="Steps">Its steps, in declared order.</param>
/// <param name="Template">
/// The template that contributed this element, as a workspace-relative path; empty when the file
/// being viewed declares it itself. An element from a template is not editable through the
/// diagram, because its text lives in another file (Requirement 5.4).
/// </param>
/// <param name="Lines">The lines declaring it, in whichever file that is.</param>
public sealed record PipelineJob(
    string Id,
    string Name,
    string DisplayName,
    bool IsDeployment,
    string Environment,
    PipelineStrategy Strategy,
    PipelinePool Pool,
    PipelineExecution Execution,
    IReadOnlyList<string> DependsOn,
    bool DependsOnDeclared,
    string Gate,
    bool IsImplicit,
    IReadOnlyList<PipelineStep> Steps,
    string Template,
    PipelineLineRange Lines)
{
    /// <summary>Whether this element came from a template, and so may not be edited here.</summary>
    public bool IsFromTemplate => Template.Length > 0;

    /// <summary>What to show for this job.</summary>
    public string Label => DisplayName.Length > 0 ? DisplayName : Name.Length > 0 ? Name : "job";
}
