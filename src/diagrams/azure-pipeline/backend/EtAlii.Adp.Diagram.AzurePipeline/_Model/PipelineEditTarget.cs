namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// An element as the writer sees it: which lines to edit, and whether editing them is allowed at
/// all.
/// </summary>
/// <remarks>
/// <para>
/// The writer takes this rather than a bare line range, because two kinds of element have a range
/// that points at real lines and yet must not be written to. An <b>implicit</b> stage or job is not
/// in the file - the schema conjured it around a bare <c>jobs</c> or <c>steps</c> list - so its
/// range covers lines belonging to something else, and setting a property on it would quietly edit
/// that something else. An element from a <b>template</b> has its text in another file entirely, so
/// an edit here would land in the wrong place (Requirement 5.4).
/// </para>
/// <para>
/// Both cases are caught in the UI before a user can reach them. This is the second line: a range
/// on its own cannot tell the writer which of them it is looking at, and the failure mode is a
/// silently corrupted build definition.
/// </para>
/// </remarks>
/// <param name="Id">The element, for saying which one was refused.</param>
/// <param name="Lines">The lines declaring it.</param>
/// <param name="IsImplicit">Whether the schema implied it rather than the file declaring it.</param>
/// <param name="Template">The template it came from, if any.</param>
public sealed record PipelineEditTarget(string Id, PipelineLineRange Lines, bool IsImplicit, string Template)
{
    /// <summary>The stage, as something that may or may not be edited.</summary>
    public static PipelineEditTarget For(PipelineStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return new PipelineEditTarget(stage.Id, stage.Lines, stage.IsImplicit, stage.Template);
    }

    /// <summary>The job, as something that may or may not be edited.</summary>
    public static PipelineEditTarget For(PipelineJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new PipelineEditTarget(job.Id, job.Lines, job.IsImplicit, job.Template);
    }

    /// <summary>The step, as something that may or may not be edited.</summary>
    public static PipelineEditTarget For(PipelineStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new PipelineEditTarget(step.Id, step.Lines, IsImplicit: false, step.Template);
    }

    /// <summary>Whether this element's own text is in this file, and so may be rewritten here.</summary>
    public bool IsEditable => !IsImplicit && Template.Length == 0;

    /// <summary>Why it may not be edited, in the terms the user would put it; empty when it may.</summary>
    public string ReadOnlyReason => this switch
    {
        { IsImplicit: true } => "This is implied by the pipeline's shape rather than written down, so there is nothing here to edit.",
        { Template.Length: > 0 } => $"This comes from {Template}, so it has to be edited there.",
        _ => "",
    };
}
