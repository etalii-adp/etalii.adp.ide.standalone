

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.AzurePipeline;

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
    int ToIndex) : ICommand;

internal sealed class MovePipelineStepCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<MovePipelineStepCommand>
{
    public Task<CommandResult> ExecuteAsync(MovePipelineStepCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.RootPath, command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(entry.Error));
        }

        var location = PipelineEdits.Locate(entry.Model, command.StepId);
        if (location is null || location.Kind != PipelineElementLocationKind.Step)
        {
            return Task.FromResult(CommandResult.Failure("That step is no longer in this pipeline."));
        }

        if (PipelineEdits.RefusalFor(location) is { Length: > 0 } refusal)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var job = location.Job!;
        var steps = job.Steps;
        var from = steps.ToList().FindIndex(step => step.Id == command.StepId);
        var to = Math.Clamp(command.ToIndex, 0, steps.Count - 1);
        if (from < 0 || from == to)
        {
            return Task.FromResult(CommandResult.Success());
        }

        if (steps.Any(step => step.IsFromTemplate))
        {
            // Some of what runs here is written in another file, so the order on the canvas is
            // not the order in this one and moving by position would move the wrong lines.
            return Task.FromResult(CommandResult.Failure(
                "Some of this job's steps come from a template, so their order cannot be changed here."));
        }

        // The step it should follow: the one currently at the destination when moving down, and
        // the one before it when moving up. Null means it becomes the first.
        var after = to > from ? steps[to] : to == 0 ? null : steps[to - 1];
        var listKeyLine = FirstStepLine(entry, job) - 1;

        if (!new PipelineWriter(entry.Document).MoveElement(
                location.Target,
                after is null ? null : PipelineEditTarget.For(after),
                listKeyLine))
        {
            return Task.FromResult(CommandResult.Success());
        }

        if (documents.Save(command.RootPath, command.BodyPath) is { Length: > 0 } error)
        {
            return Task.FromResult(CommandResult.Failure(error));
        }

        // Undo moves it back to where it was. Ids are positional, so the step that is now at
        // `to` is the one to send home - which is the same step, under its new id.
        return Task.FromResult(CommandResult.Success(new MovePipelineStepCommand(
            command.RootPath,
            command.BodyPath,
            $"{job.Id}/step-{to}",
            from)));
    }

    /// <summary>The line the job's first step sits on, which is where the front of the list is.</summary>
    private static int FirstStepLine(PipelineDocumentEntry entry, PipelineJob job)
    {
        _ = entry;
        return job.Steps[0].Lines.Start;
    }
}
