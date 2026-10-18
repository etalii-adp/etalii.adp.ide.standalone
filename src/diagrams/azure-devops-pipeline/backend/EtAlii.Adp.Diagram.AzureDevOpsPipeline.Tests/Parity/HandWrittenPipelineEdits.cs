using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The rename, the enabled switch, the dependencies, the adds, the removal and the move of a step as the module's
/// handlers wrote them by hand before they were run from the DISL definition and planned by the persistence plugin
/// (azure-devops-pipeline-disl-fbl task 10): the handlers' bodies, over the <see cref="PipelineWriter"/>,
/// <see cref="PipelineBlocks"/> and <see cref="PipelineEdits"/> they called, moved here from the product with them, kept
/// as the oracle the definition's edits are compared with. Moved unchanged but for the shape of the calls: each takes
/// the body and the model it was read as, edits the body, and gives the refusal, or null where it wrote or found
/// nothing to write; the undo each built is the host's span restore now and is not kept.
/// </summary>
internal static class HandWrittenPipelineEdits
{
    /// <summary><c>RenamePipelineElementCommandHandler</c>'s edit.</summary>
    public static string? Rename(LineDocument body, PipelineModel model, string elementId, string displayName)
    {
        var location = PipelineEdits.Locate(model, elementId);
        if (Refuses(location) is { } refusal) return refusal;

        new PipelineWriter(body).SetDisplayName(location!.Target, displayName);
        return null;
    }

    /// <summary><c>SetPipelineElementEnabledCommandHandler</c>'s edit.</summary>
    public static string? SetEnabled(LineDocument body, PipelineModel model, string elementId, bool enabled)
    {
        var location = PipelineEdits.Locate(model, elementId);
        if (Refuses(location) is { } refusal) return refusal;

        new PipelineWriter(body).SetEnabled(location!.Target, enabled ? "" : "false");
        return null;
    }

    /// <summary><c>SetPipelineDependenciesCommandHandler</c>'s edit.</summary>
    public static string? SetDependencies(LineDocument body, PipelineModel model, string elementId, IReadOnlyList<string> dependsOn, bool declared)
    {
        var location = PipelineEdits.Locate(model, elementId);
        if (location?.Kind == PipelineElementLocationKind.Step)
        {
            // Steps are a sequence, not a graph - they run in the order they are written and have
            // no dependsOn to set.
            return "Steps run in order, so a step has nothing to wait for.";
        }

        if (Refuses(location) is { } refusal) return refusal;

        var names = PipelineEdits.NamesToIds(model, location!);
        if ((dependsOn.FirstOrDefault(name => !names.ContainsKey(name)) ?? "") is { Length: > 0 } unknown)
        {
            return $"This pipeline has nothing called '{unknown}' to wait for.";
        }

        if (PipelineEdits.WouldCycle(model, elementId, dependsOn))
        {
            return $"That would make {location!.Label} wait for something that is already waiting for it.";
        }

        if (location!.Kind == PipelineElementLocationKind.Stage && declared && PipelineEdits.WouldLeaveNoStartingStage(model, elementId, dependsOn))
        {
            return "A pipeline must contain at least one stage with no dependencies, or it has nothing to start with.";
        }

        var writer = new PipelineWriter(body);
        _ = declared ? writer.SetDependsOn(location.Target, dependsOn) : writer.ClearDependsOn(location.Target);
        return null;
    }

    /// <summary><c>AddPipelineElementCommandHandler</c>'s edit.</summary>
    public static string? Add(LineDocument body, PipelineModel model, PipelineAddKind kind, string parentId, string name = "")
    {
        var added = kind == PipelineAddKind.Stage ? AddStage(body, model, name) : AddWithin(body, model, kind, parentId, name);
        return added.Error.Length > 0 ? added.Error : null;
    }

    /// <summary><c>RemovePipelineElementCommandHandler</c>'s edit.</summary>
    public static string? Remove(LineDocument body, PipelineModel model, string elementId)
    {
        var location = PipelineEdits.Locate(model, elementId);
        if (Refuses(location) is { } refusal) return refusal;

        if (RefuseRemoval(model, location!) is { Length: > 0 } reason) return reason;

        _ = new PipelineWriter(body).RemoveElement(location!.Target);
        return null;
    }

    /// <summary><c>MovePipelineStepCommandHandler</c>'s edit.</summary>
    public static string? MoveStep(LineDocument body, PipelineModel model, string stepId, int toIndex)
    {
        var location = PipelineEdits.Locate(model, stepId);
        if (location is null || location.Kind != PipelineElementLocationKind.Step)
        {
            return "That step is no longer in this pipeline.";
        }

        if (PipelineEdits.RefusalFor(location) is { Length: > 0 } refusal) return refusal;

        var job = location.Job!;
        var steps = job.Steps;
        var from = steps.ToList().FindIndex(step => step.Id == stepId);
        var to = Math.Clamp(toIndex, 0, steps.Count - 1);
        if (from < 0 || from == to) return null;

        if (steps.Any(step => step.IsFromTemplate))
        {
            return "Some of this job's steps come from a template, so their order cannot be changed here.";
        }

        var after = to > from ? steps[to] : to == 0 ? null : steps[to - 1];
        var listKeyLine = steps[0].Lines.Start - 1;
        _ = new PipelineWriter(body).MoveElement(location.Target, after is null ? null : PipelineEditTarget.For(after), listKeyLine);
        return null;
    }

    /// <summary><c>PipelineEditHandler.Refuses</c>: the element is gone, or it is written in a template or implied.</summary>
    private static string? Refuses(PipelineElementLocation? location) =>
        location is null
            ? "That element is no longer in this pipeline."
            : PipelineEdits.RefusalFor(location) is { Length: > 0 } reason
            ? reason
            : null;

    private static string RefuseRemoval(PipelineModel model, PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage when model.Stages.Count(stage => !stage.IsImplicit) <= 1 =>
            "A pipeline needs at least one stage.",
        PipelineElementLocationKind.Stage when
            PipelineEdits.WouldLeaveNoStartingStage(model with { Stages = model.Stages.Where(candidate => candidate != location.Stage).ToList() }, "", []) =>
            "A pipeline must contain at least one stage with no dependencies, and this is the only one.",
        PipelineElementLocationKind.Job when location.Stage.Jobs.Count <= 1 =>
            $"{location.Stage.Label} needs at least one job.",
        PipelineElementLocationKind.Step when location.Job!.Steps.Count <= 1 =>
            $"{location.Job.Label} needs at least one step.",
        _ => "",
    };

    private static PipelineAddOutcome AddStage(LineDocument body, PipelineModel model, string wanted)
    {
        var declared = model.Stages.Where(stage => stage is { IsImplicit: false, IsFromTemplate: false }).ToList();
        if (declared.Count == 0)
        {
            return PipelineAddOutcome.Failed("This pipeline has no stages block to add a stage to. Add one in the file first.");
        }

        var name = PipelineBlocks.UnusedName(wanted.Length > 0 ? wanted : "NewStage", model.Stages.Select(stage => stage.Name));
        var last = declared[^1];
        var indent = body.Lines[last.Lines.Start].Indent();
        new PipelineWriter(body).InsertElement(PipelineEditTarget.For(last), listKeyLine: 0, PipelineBlocks.Stage(name, indent));
        return PipelineAddOutcome.Added(name);
    }

    private static PipelineAddOutcome AddWithin(LineDocument body, PipelineModel model, PipelineAddKind kind, string parentId, string name)
    {
        var parent = PipelineEdits.Locate(model, parentId);
        return parent is null
            ? PipelineAddOutcome.Failed("That element is no longer in this pipeline.")
            : PipelineEdits.RefusalFor(parent) is { Length: > 0 } refusal
            ? PipelineAddOutcome.Failed(refusal)
            : kind switch
            {
                PipelineAddKind.Step => AddStep(body, parent),
                _ => AddJob(body, model, kind, name, parent),
            };
    }

    private static PipelineAddOutcome AddJob(LineDocument body, PipelineModel model, PipelineAddKind kind, string wanted, PipelineElementLocation parent)
    {
        if (parent.Kind != PipelineElementLocationKind.Stage)
        {
            return PipelineAddOutcome.Failed("A job goes in a stage.");
        }

        var stage = parent.Stage;
        var authored = stage.Jobs.Where(job => !job.IsFromTemplate).ToList();
        if (authored.Count == 0)
        {
            return PipelineAddOutcome.Failed("This stage has no jobs of its own to add one beside.");
        }

        var name = PipelineBlocks.UnusedName(wanted.Length > 0 ? wanted : "NewJob", model.Jobs.Select(job => job.Name));
        var last = authored[^1];
        var indent = body.Lines[last.Lines.Start].Indent();
        var lines = kind == PipelineAddKind.DeploymentJob
            ? PipelineBlocks.DeploymentJob(name, name.ToLowerInvariant(), indent)
            : PipelineBlocks.Job(name, indent);

        new PipelineWriter(body).InsertElement(PipelineEditTarget.For(last), listKeyLine: 0, lines);
        return PipelineAddOutcome.Added($"{stage.Id}/{name}");
    }

    private static PipelineAddOutcome AddStep(LineDocument body, PipelineElementLocation parent)
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
        var indent = body.Lines[last.Lines.Start].Indent();
        new PipelineWriter(body).InsertElement(PipelineEditTarget.For(last), listKeyLine: 0, PipelineBlocks.Step(PipelineBlocks.PlaceholderScript, indent));
        return PipelineAddOutcome.Added($"{job.Id}/step-{job.Steps.Count}");
    }
}
