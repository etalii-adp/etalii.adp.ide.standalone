namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// An element found in a model, together with everything around it a command needs.
/// </summary>
/// <remarks>
/// Commands work on ids, and every one of them has to answer the same questions: what kind of
/// thing is this, which stage and job is it in, and may it be edited at all. Answering them once
/// here means a handler reads as the edit it performs rather than as a search followed by an edit.
/// </remarks>
/// <param name="Kind">Whether the id named a stage, a job or a step.</param>
/// <param name="Stage">The stage it is, or the stage it is in.</param>
/// <param name="Job">The job it is, or the job it is in; null for a stage.</param>
/// <param name="Step">The step it is; null for a stage or a job.</param>
public sealed record PipelineElementLocation(
    PipelineElementLocationKind Kind,
    PipelineStage Stage,
    PipelineJob? Job,
    PipelineStep? Step)
{
    /// <summary>The element as the writer sees it, including whether it may be written to.</summary>
    public PipelineEditTarget Target => Kind switch
    {
        PipelineElementLocationKind.Stage => PipelineEditTarget.For(Stage),
        PipelineElementLocationKind.Job => PipelineEditTarget.For(Job!),
        _ => PipelineEditTarget.For(Step!),
    };

    /// <summary>Its id.</summary>
    public string Id => Kind switch
    {
        PipelineElementLocationKind.Stage => Stage.Id,
        PipelineElementLocationKind.Job => Job!.Id,
        _ => Step!.Id,
    };

    /// <summary>What to call it in a message meant for a person.</summary>
    public string Label => Kind switch
    {
        PipelineElementLocationKind.Stage => Stage.Label,
        PipelineElementLocationKind.Job => Job!.Label,
        _ => Step!.Label,
    };
}
