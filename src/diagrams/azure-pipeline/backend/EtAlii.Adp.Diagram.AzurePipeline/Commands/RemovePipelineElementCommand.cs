using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Removes a stage, a job or a step.
/// </summary>
/// <remarks>
/// Its inverse is not an add. An add writes a fresh block from a template and would bring back
/// something that merely resembled what was removed - a stage with its conditions, its pool and
/// its comments gone. So the undo puts the exact lines back where they were, which is the only
/// version of "undo" that is true for a file this module promises not to reformat.
/// </remarks>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">What to remove.</param>
public sealed record RemovePipelineElementCommand(
    string RootPath,
    string BodyPath,
    string ElementId) : ICommand;

/// <summary>
/// Puts removed lines back exactly as they were - the inverse of a remove.
/// </summary>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="AtLine">The line the removed block started on.</param>
/// <param name="Lines">The lines themselves.</param>
public sealed record RestorePipelineLinesCommand(
    string RootPath,
    string BodyPath,
    int AtLine,
    IReadOnlyList<string> Lines) : ICommand;

internal sealed class RemovePipelineElementCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<RemovePipelineElementCommand>
{
    public Task<CommandResult> ExecuteAsync(RemovePipelineElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.RootPath, command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(entry.Error));
        }

        var location = PipelineEdits.Locate(entry.Model, command.ElementId);
        if (location is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this pipeline."));
        }

        if (PipelineEdits.RefusalFor(location) is { Length: > 0 } refusal)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        if (Refuse(entry.Model, location) is { Length: > 0 } reason)
        {
            return Task.FromResult(CommandResult.Failure(reason));
        }

        var target = location.Target;
        var removed = Enumerable.Range(target.Lines.Start, target.Lines.Length)
            .Select(index => entry.Document.Lines[index].Text)
            .ToList();

        new PipelineWriter(entry.Document).RemoveElement(target);
        var error = documents.Save(command.RootPath, command.BodyPath, entry);
        return Task.FromResult(error.Length > 0
            ? CommandResult.Failure(error)
            : CommandResult.Success(new RestorePipelineLinesCommand(
                command.RootPath,
                command.BodyPath,
                target.Lines.Start,
                removed)));
    }

    /// <summary>
    /// Why this element must stay, or empty when it may go.
    /// </summary>
    /// <remarks>
    /// The schema requires a stage to have a job and a job to have a step, so removing the last
    /// one leaves a file that will not queue. Refusing is better than writing it: the diagram
    /// would have appeared to work and the pipeline would be broken (Requirement 9.4).
    /// </remarks>
    private static string Refuse(PipelineModel model, PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage when model.Stages.Count(stage => !stage.IsImplicit) <= 1 =>
            "A pipeline needs at least one stage.",
        PipelineElementLocationKind.Stage when
            PipelineEdits.WouldLeaveNoStartingStage(WithoutStage(model, location.Stage), "", []) =>
            "A pipeline must contain at least one stage with no dependencies, and this is the only one.",
        PipelineElementLocationKind.Job when location.Stage.Jobs.Count <= 1 =>
            $"{location.Stage.Label} needs at least one job.",
        PipelineElementLocationKind.Step when location.Job!.Steps.Count <= 1 =>
            $"{location.Job.Label} needs at least one step.",
        _ => "",
    };

    /// <summary>The model as it would be with one stage gone, for asking what that would leave.</summary>
    private static PipelineModel WithoutStage(PipelineModel model, PipelineStage stage) =>
        model with { Stages = model.Stages.Where(candidate => candidate != stage).ToList() };
}

internal sealed class RestorePipelineLinesCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<RestorePipelineLinesCommand>
{
    public Task<CommandResult> ExecuteAsync(RestorePipelineLinesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.RootPath, command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(entry.Error));
        }

        if (command.AtLine > entry.Document.Lines.Count)
        {
            // The file has been cut down under us since the removal, so there is no longer a line
            // to put these back at. Refusing is honest; guessing a position is not.
            return Task.FromResult(CommandResult.Failure(
                "This pipeline has changed too much since then to put that back."));
        }

        new PipelineWriter(entry.Document).InsertElement(null, Math.Max(command.AtLine - 1, 0), command.Lines);
        var error = documents.Save(command.RootPath, command.BodyPath, entry);
        if (error.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(error));
        }

        // Redoing the undo removes it again - and the element is found by id, which is what it
        // had before it was removed and has again now.
        var restored = PipelineEdits.Locate(
            documents.GetOrLoad(command.RootPath, command.BodyPath).Model,
            IdOf(command.Lines));

        return Task.FromResult(restored is null
            ? CommandResult.Success()
            : CommandResult.Success(new RemovePipelineElementCommand(command.RootPath, command.BodyPath, restored.Id)));
    }

    /// <summary>
    /// The name a restored block declares, which is how the element is found again afterwards.
    /// </summary>
    /// <remarks>
    /// Read from the text rather than carried on the command, because the text is the thing that
    /// was actually put back - anything else could disagree with it.
    /// </remarks>
    private static string IdOf(IReadOnlyList<string> lines)
    {
        var first = lines.Count > 0 ? lines[0].TrimStart(' ', '-', ' ') : "";
        var colon = first.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? "" : first[(colon + 1)..].Trim();
    }
}
