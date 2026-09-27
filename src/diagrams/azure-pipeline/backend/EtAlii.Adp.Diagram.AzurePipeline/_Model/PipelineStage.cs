using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// One stage, as declared - or the single implicit one the schema defines around a file that has
/// no <c>stages</c> key (Requirement 4.2).
/// </summary>
/// <param name="Id">Stable within a document: the stage's name, or its position when it has none.</param>
/// <param name="Name">Its <c>stage:</c> value; empty for the implicit stage.</param>
/// <param name="DisplayName">Its <c>displayName</c>, where it has one.</param>
/// <param name="Pool">Where its jobs run unless they say otherwise (Requirement 4.7).</param>
/// <param name="Execution">What decides whether and how it runs (Requirement 4.5).</param>
/// <param name="TriggerIsManual">Whether it declares <c>trigger: manual</c>, so it waits to be started by hand.</param>
/// <param name="IsSkippable">
/// Its <c>isSkippable</c>, verbatim. <c>false</c> means the stage runs even when a run is
/// otherwise skipped, which is exactly the kind of thing a reader of the diagram is asking.
/// </param>
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
/// <param name="Template">
/// The template that contributed this element, as a workspace-relative path; empty when the file
/// being viewed declares it itself. An element from a template is not editable through the
/// diagram, because its text lives in another file (Requirement 5.4).
/// </param>
/// <param name="Lines">The lines declaring it, in whichever file that is.</param>
public sealed record PipelineStage(
    string Id,
    string Name,
    string DisplayName,
    PipelinePool Pool,
    PipelineExecution Execution,
    bool TriggerIsManual,
    string IsSkippable,
    IReadOnlyList<string> DependsOn,
    bool DependsOnDeclared,
    string Gate,
    bool IsImplicit,
    IReadOnlyList<PipelineJob> Jobs,
    string Template,
    LineRange Lines)
{
    /// <summary>Whether this element came from a template, and so may not be edited here.</summary>
    public bool IsFromTemplate => Template.Length > 0;

    /// <summary>What to show for this stage.</summary>
    public string Label => DisplayName.Length > 0 ? DisplayName : Name.Length > 0 ? Name : "stage";
}
