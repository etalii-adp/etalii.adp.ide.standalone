using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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
    /// <remarks>
    /// <para>
    /// <b>The definition's built-in reorder</b> (<c>moveUp</c> and <c>moveDown</c> are menu entries, not operations):
    /// the step reparented into its own job before the step it is to end up in front of, which the plugin plans as
    /// the step's entry cut out and written in again there. Its undo puts back the bytes the move replaced, so the
    /// step returns to where it was with the blank lines and comments around it as they were.
    /// </para>
    /// <para>
    /// <b>Refused by the definition's reorder rule</b> (<c>templateStepsKeepOrder</c>), after the step is found, is one
    /// this file writes and is asked to go somewhere else.
    /// </para>
    /// </remarks>
    protected override EditOutcome Apply(PipelineBody body, MovePipelineStepCommand command)
    {
        if (PipelineDefinition.ElementOf(body, command.StepId) is not { } step || !step.IsA("Step") || step.Parent is not { } job)
        {
            return EditOutcome.Refused("That step is no longer in this pipeline.");
        }

        if (step.ValueOf("editable") is false)
        {
            return EditOutcome.Refused(step.ValueOf("readOnlyReason") as string ?? "");
        }

        var steps = job.Children.Where(child => child.IsA("Step")).ToList();
        var from = steps.IndexOf(step);
        var to = Math.Clamp(command.ToIndex, 0, steps.Count - 1);
        if (from < 0 || from == to)
        {
            return EditOutcome.Unchanged;
        }

        if (PipelineDefinition.ReorderRefusal(step, steps, from, to) is { } refused)
        {
            // Some of what runs here is written in another file, so the order on the canvas is
            // not the order in this one and moving by position would move the wrong lines.
            return EditOutcome.Refused(refused);
        }

        // Before the step now at the destination when moving up, and before the one after it when moving down: the
        // index counts the step itself where it is now. Past the last step is the end of the list.
        var before = to > from ? to + 1 : to;
        var index = before >= steps.Count ? -1 : before;
        return PipelineDefinition.Apply(body, new DislTransaction([new DislChange.Reparent(step.Id, job.Id, index)], [], null));
    }
}
