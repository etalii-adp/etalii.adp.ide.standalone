namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// A template this module did not follow, and the reason (Requirement 5.3).
/// </summary>
/// <param name="Reference">The reference as the file wrote it.</param>
/// <param name="Reason">Why it was not followed.</param>
public sealed record PipelineTemplateUnresolved(
    PipelineTemplateReference Reference,
    PipelineTemplateUnresolvedReason Reason)
{
    /// <summary>
    /// What to tell the reader, in the terms they wrote the file in.
    /// </summary>
    public string Explanation => Reason switch
    {
        PipelineTemplateUnresolvedReason.OtherRepository =>
            $"It comes from the '{Reference.Resource}' repository resource, which is not checked out here.",
        PipelineTemplateUnresolvedReason.OutsideWorkspace =>
            "Its path leads outside this workspace, so it was not read.",
        PipelineTemplateUnresolvedReason.ParameterDependent =>
            "Its path depends on a parameter, so which file it means is decided at compile time.",
        PipelineTemplateUnresolvedReason.NotFound =>
            $"There is no file at '{Reference.Path}'.",
        PipelineTemplateUnresolvedReason.Unreadable =>
            $"'{Reference.Path}' could not be read as a pipeline.",
        PipelineTemplateUnresolvedReason.Cyclic =>
            $"'{Reference.Path}' includes itself, directly or through another template.",
        _ => "",
    };
}
