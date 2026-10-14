using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Moves a step to another position within its job.
/// </summary>
/// <remarks>
/// Steps are the one thing in a pipeline whose order is the whole of their meaning: they run top
/// to bottom and there is no <c>dependsOn</c> to say otherwise (Requirement 6.4). So reordering
/// them is a real edit, where dragging a stage is not - a stage's position on the canvas is
/// derived from what it waits for, and moving one would have nowhere to be written down.
/// </remarks>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="StepId">The step to move.</param>
/// <param name="ToIndex">Where it should end up among its job's steps, counting from zero.</param>
public sealed record MovePipelineStepCommand(
    string RootPath,
    string BodyPath,
    string StepId,
    int ToIndex) : IPipelineEditCommand;

internal sealed class MovePipelineStepCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<MovePipelineStepCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, PipelineModel model, MovePipelineStepCommand command)
    {
        var location = PipelineEdits.Locate(model, command.StepId);
        if (location is null || location.Kind != PipelineElementLocationKind.Step)
        {
            return EditOutcome.Refused("That step is no longer in this pipeline.");
        }

        if (PipelineEdits.RefusalFor(location) is { Length: > 0 } refusal)
        {
            return EditOutcome.Refused(refusal);
        }

        var job = location.Job!;
        var steps = job.Steps;
        var from = steps.ToList().FindIndex(step => step.Id == command.StepId);
        var to = Math.Clamp(command.ToIndex, 0, steps.Count - 1);
        if (from < 0 || from == to)
        {
            return EditOutcome.Unchanged;
        }

        if (steps.Any(step => step.IsFromTemplate))
        {
            // Some of what runs here is written in another file, so the order on the canvas is
            // not the order in this one and moving by position would move the wrong lines.
            return EditOutcome.Refused("Some of this job's steps come from a template, so their order cannot be changed here.");
        }

        // The step it should follow: the one currently at the destination when moving down, and
        // the one before it when moving up. Null means it becomes the first. The list's key is the
        // line above the job's first step, which is where the front of the list is.
        var after = to > from ? steps[to] : to == 0 ? null : steps[to - 1];
        var listKeyLine = steps[0].Lines.Start - 1;

        // Undo moves it back to where it was. Ids are positional, so the step that is now at
        // `to` is the one to send home - which is the same step, under its new id.
        return new PipelineWriter(body).MoveElement(
                location.Target,
                after is null ? null : PipelineEditTarget.For(after),
                listKeyLine)
            ? EditOutcome.UndoneBy(command with { StepId = $"{job.Id}/step-{to}", ToIndex = from })
            : EditOutcome.Unchanged;
    }
}
