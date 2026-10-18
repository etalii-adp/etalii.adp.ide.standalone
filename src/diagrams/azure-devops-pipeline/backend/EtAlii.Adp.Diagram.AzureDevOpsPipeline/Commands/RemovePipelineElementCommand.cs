using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Removes a stage, a job or a step.
/// </summary>
/// <remarks>
/// Its inverse is not an add. An add writes a fresh entry from the definition and would bring back
/// something that merely resembled what was removed - a stage with its conditions, its pool and
/// its comments gone. So the undo puts back the exact bytes the removal took, which is the only
/// version of "undo" that is true for a file this module promises not to reformat.
/// </remarks>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">What to remove.</param>
public sealed record RemovePipelineElementCommand(
    string RootPath,
    string BodyPath,
    string ElementId) : IPipelineEditCommand;

internal sealed class RemovePipelineElementCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<RemovePipelineElementCommand>(documents)
{
    /// <remarks>
    /// <para>
    /// <b>Refused by the definition's deletion rules</b> (<c>keepLastStage</c>, <c>keepTheOnlyStart</c>,
    /// <c>keepLastJob</c> and <c>keepLastStep</c>), after the element is found and is one this file writes: a stage,
    /// a job and a step must each keep a sibling, and the stages left must keep one that waits for nothing, so the
    /// pipeline still queues (Requirement 9.4).
    /// </para>
    /// <para>
    /// <b>Then the definition's deletion policy</b>, with the children written inside the entry, so the removal of the
    /// element is the one change written: what ends at it is drawn from what it holds, and a name another element
    /// depends on stays as written, as the definition's own findings then report.
    /// </para>
    /// </remarks>
    protected override EditOutcome Apply(PipelineBody body, RemovePipelineElementCommand command)
    {
        if (PipelineDefinition.ElementOf(body, command.ElementId) is not { } element)
        {
            return EditOutcome.Refused(PipelineDefinition.Gone);
        }

        if (element.ValueOf("editable") is false)
        {
            return EditOutcome.Refused(element.ValueOf("readOnlyReason") as string ?? "");
        }

        if (PipelineDefinition.DeleteRefusal(element) is { } refused)
        {
            return EditOutcome.Refused(refused);
        }

        var removal = DeletionPolicy.Changes(PipelineDefinition.Specification, element, nested: true);
        return PipelineDefinition.Apply(body, removal.WasApplied
            ? removal with { Changes = [.. removal.Changes.OfType<DislChange.Remove>().Where(remove => remove.ElementId == element.Id)] }
            : removal);
    }
}
