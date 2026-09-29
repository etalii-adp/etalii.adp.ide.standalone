namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What a pipeline file contains, in Azure Pipelines' own vocabulary (Requirement 4.1).
/// </summary>
/// <param name="Stages">
/// The stages, in declared order - always at least one for a file that runs anything, since a
/// file with only <c>jobs</c> or only <c>steps</c> gets the implicit stage the schema defines.
/// </param>
/// <param name="Templates">
/// Every <c>template</c> reference standing where a stage or job would, each carrying the id of
/// the element it sits inside. Step-level templates are steps of kind
/// <see cref="PipelineStepKind.Template"/> instead, since that is where they appear.
/// </param>
/// <param name="Extends">
/// The <c>extends</c> reference, where the file has one. A file that extends declares no stages of
/// its own: its real shape is the template's, and what it contributes is parameters
/// (Requirement 5.5).
/// </param>
/// <param name="Unresolved">
/// The templates that were not followed, each with its reason (Requirement 5.3). Empty is not the
/// same as "no templates": a reference that was followed appears in <paramref name="Templates"/>
/// and not here.
/// </param>
public sealed record PipelineModel(
    IReadOnlyList<PipelineStage> Stages,
    IReadOnlyList<PipelineTemplateReference> Templates,
    PipelineTemplateReference? Extends,
    IReadOnlyList<PipelineTemplateUnresolved> Unresolved)
{
    /// <summary>A file that parsed but declares nothing this module models.</summary>
    public static PipelineModel Empty { get; } = new([], [], null, []);

    /// <summary>Every job in the pipeline, stage order then declared order.</summary>
    public IEnumerable<PipelineJob> Jobs => Stages.SelectMany(stage => stage.Jobs);

    /// <summary>Every step in the pipeline, job order then declared order.</summary>
    public IEnumerable<PipelineStep> Steps => Jobs.SelectMany(job => job.Steps);
}
