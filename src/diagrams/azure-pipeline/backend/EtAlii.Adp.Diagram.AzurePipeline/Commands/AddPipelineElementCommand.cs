using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Adds a stage, a job or a step, at the end of whatever list it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// One command for all three, because they differ only in which list they join and what text they
/// are - and the toolbox, the context menu and a keyboard shortcut all have to end up executing
/// the same thing (Requirement 9.6).
/// </para>
/// <para>
/// The name is decided by the handler rather than carried by the command, except when the command
/// names one - which is what makes undo and redo land on the same element. Redoing an add has to
/// recreate the element the undo removed, and an add that picked a fresh name each time would
/// create a second one.
/// </para>
/// </remarks>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="Kind">What to add.</param>
/// <param name="ParentId">The stage a job joins, or the job a step joins; empty for a stage.</param>
/// <param name="Name">The name to use, or empty to pick a free one.</param>
public sealed record AddPipelineElementCommand(
    string RootPath,
    string BodyPath,
    PipelineAddKind Kind,
    string ParentId,
    string Name = "") : ICommand;

internal sealed class AddPipelineElementCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<AddPipelineElementCommand>
{
    public Task<CommandResult> ExecuteAsync(AddPipelineElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.RootPath, command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(entry.Error));
        }

        var added = command.Kind == PipelineAddKind.Stage
            ? AddStage(entry, command)
            : AddWithin(entry, command);

        if (added.Error.Length > 0)
        {
            return Task.FromResult(CommandResult.Failure(added.Error));
        }

        var saved = documents.Save(command.RootPath, command.BodyPath, entry);
        return Task.FromResult(saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(new RemovePipelineElementCommand(command.RootPath, command.BodyPath, added.ElementId)));
    }

    /// <summary>
    /// A stage joins the pipeline's <c>stages</c> list, after the last stage there is.
    /// </summary>
    /// <remarks>
    /// A file with no <c>stages</c> key is refused rather than restructured. Turning a jobs-only
    /// pipeline into a staged one means re-indenting everything in it, which is a rewrite of the
    /// whole file and exactly what this module promises not to do to one.
    /// </remarks>
    private static PipelineAddOutcome AddStage(PipelineDocumentEntry entry, AddPipelineElementCommand command)
    {
        var declared = entry.Model.Stages.Where(stage => !stage.IsImplicit && !stage.IsFromTemplate).ToList();
        if (declared.Count == 0)
        {
            return PipelineAddOutcome.Failed(
                "This pipeline has no stages block to add a stage to. Add one in the file first.");
        }

        var name = PipelineBlocks.UnusedName(
            command.Name.Length > 0 ? command.Name : "NewStage",
            entry.Model.Stages.Select(stage => stage.Name));

        var last = declared[^1];
        var indent = entry.Document.Lines[last.Lines.Start].Indent();
        new PipelineWriter(entry.Document).InsertElement(
            PipelineEditTarget.For(last),
            listKeyLine: 0,
            PipelineBlocks.Stage(name, indent));

        return PipelineAddOutcome.Added(name);
    }

    /// <summary>A job joins a stage's <c>jobs</c>, a step joins a job's <c>steps</c>.</summary>
    private static PipelineAddOutcome AddWithin(PipelineDocumentEntry entry, AddPipelineElementCommand command)
    {
        var parent = PipelineEdits.Locate(entry.Model, command.ParentId);
        return parent is null
            ? PipelineAddOutcome.Failed("That element is no longer in this pipeline.")
            : PipelineEdits.RefusalFor(parent) is { Length: > 0 } refusal
            ? PipelineAddOutcome.Failed(refusal)
            : command.Kind switch
            {
                PipelineAddKind.Step => AddStep(entry, parent),
                _ => AddJob(entry, command, parent),
            };
    }

    private static PipelineAddOutcome AddJob(
        PipelineDocumentEntry entry,
        AddPipelineElementCommand command,
        PipelineElementLocation parent)
    {
        if (parent.Kind != PipelineElementLocationKind.Stage)
        {
            return PipelineAddOutcome.Failed("A job goes in a stage.");
        }

        var stage = parent.Stage;
        var authored = stage.Jobs.Where(job => !job.IsFromTemplate).ToList();
        if (authored.Count == 0)
        {
            // Every stage this module can add has a job in it already, so a stage with none is one
            // whose jobs all came from a template - and there is nowhere here to put another.
            return PipelineAddOutcome.Failed("This stage has no jobs of its own to add one beside.");
        }

        var name = PipelineBlocks.UnusedName(
            command.Name.Length > 0 ? command.Name : "NewJob",
            entry.Model.Jobs.Select(job => job.Name));

        var last = authored[^1];
        var indent = entry.Document.Lines[last.Lines.Start].Indent();
        var lines = command.Kind == PipelineAddKind.DeploymentJob
            ? PipelineBlocks.DeploymentJob(name, name.ToLowerInvariant(), indent)
            : PipelineBlocks.Job(name, indent);

        new PipelineWriter(entry.Document).InsertElement(PipelineEditTarget.For(last), listKeyLine: 0, lines);
        return PipelineAddOutcome.Added($"{stage.Id}/{name}");
    }

    private static PipelineAddOutcome AddStep(PipelineDocumentEntry entry, PipelineElementLocation parent)
    {
        if (parent.Kind != PipelineElementLocationKind.Job)
        {
            return PipelineAddOutcome.Failed("A step goes in a job.");
        }

        var job = parent.Job!;
        var authored = job.Steps.Where(step => !step.IsFromTemplate).ToList();
        if (authored.Count == 0)
        {
            return PipelineAddOutcome.Failed("This job has no steps of its own to add one beside.");
        }

        var last = authored[^1];
        var indent = entry.Document.Lines[last.Lines.Start].Indent();
        new PipelineWriter(entry.Document).InsertElement(
            PipelineEditTarget.For(last),
            listKeyLine: 0,
            PipelineBlocks.Step(PipelineBlocks.PlaceholderScript, indent));

        // A step has no name, so its id is its position - and the new one is at the end.
        return PipelineAddOutcome.Added($"{job.Id}/step-{job.Steps.Count}");
    }
}
