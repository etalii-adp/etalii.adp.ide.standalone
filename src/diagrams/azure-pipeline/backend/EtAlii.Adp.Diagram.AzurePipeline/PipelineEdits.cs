namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What every pipeline command needs before it can do anything: find the element, decide whether
/// it may be touched, and check the edit will not produce a pipeline that cannot run.
/// </summary>
/// <remarks>
/// Shared rather than repeated because handlers are re-runnable - undo and redo dispatch them
/// again with the recorded arguments, so each one validates its own preconditions rather than
/// trusting that somebody checked earlier. That means these checks happen on every path, and a
/// second copy of them would be a second thing to get wrong.
/// </remarks>
public static class PipelineEdits
{
    /// <summary>The element an id names, or null when the model holds no such thing.</summary>
    public static PipelineElementLocation? Locate(PipelineModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (var stage in model.Stages)
        {
            if (string.Equals(stage.Id, elementId, StringComparison.Ordinal))
            {
                return new PipelineElementLocation(PipelineElementLocationKind.Stage, stage, null, null);
            }

            foreach (var job in stage.Jobs)
            {
                if (string.Equals(job.Id, elementId, StringComparison.Ordinal))
                {
                    return new PipelineElementLocation(PipelineElementLocationKind.Job, stage, job, null);
                }

                var step = job.Steps.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
                if (step is not null)
                {
                    return new PipelineElementLocation(PipelineElementLocationKind.Step, stage, job, step);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Why this element may not be edited here, or empty when it may.
    /// </summary>
    /// <remarks>
    /// Requirement 5.4: an element from a template has its text in another file, so the edit would
    /// land in the wrong one. The message names the file, because "you cannot edit this" without
    /// saying where you can is not an answer.
    /// </remarks>
    public static string RefusalFor(PipelineElementLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.Target.IsEditable ? "" : location.Target.ReadOnlyReason;
    }

    /// <summary>
    /// Whether making <paramref name="elementId"/> depend on <paramref name="dependsOn"/> would
    /// close a loop.
    /// </summary>
    /// <remarks>
    /// Checked before the edit rather than reported after it. The graph reports a cycle it finds
    /// in a file somebody else wrote, because refusing to draw it would hide the mistake - but a
    /// cycle this diagram is about to write is one it should simply not write (Requirement 9.4).
    /// </remarks>
    public static bool WouldCycle(PipelineModel model, string elementId, IReadOnlyList<string> dependsOn)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dependsOn);

        var location = Locate(model, elementId);
        if (location is null)
        {
            return false;
        }

        var graph = location.Kind == PipelineElementLocationKind.Stage
            ? PipelineGraphBuilder.OfStages(model)
            : PipelineGraphBuilder.OfJobs(location.Stage);

        // Anything that already waits, directly or not, for the element being edited. Depending on
        // one of those is what closes the loop.
        var downstream = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([elementId]);
        while (pending.Count > 0)
        {
            foreach (var dependent in graph.DependentsOf(pending.Dequeue()))
            {
                if (downstream.Add(dependent))
                {
                    pending.Enqueue(dependent);
                }
            }
        }

        var byName = NamesToIds(model, location);
        return dependsOn.Any(name =>
            byName.TryGetValue(name, out var id) && (downstream.Contains(id) || string.Equals(id, elementId, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Whether the pipeline would still have a stage that waits for nothing.
    /// </summary>
    /// <remarks>
    /// A pipeline must contain at least one stage with no dependencies or it cannot start, so an
    /// edit that would take the last one away is refused rather than written (Requirement 9.4).
    /// The check runs on stages only: jobs default to waiting for nothing, so there is no
    /// equivalent way to strand them.
    /// </remarks>
    public static bool WouldLeaveNoStartingStage(PipelineModel model, string stageId, IReadOnlyList<string> dependsOn)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dependsOn);

        if (dependsOn.Count == 0)
        {
            // This stage is becoming a starting stage, so there is certainly one.
            return false;
        }

        var graph = PipelineGraphBuilder.OfStages(model);
        return !model.Stages.Any(stage =>
            !string.Equals(stage.Id, stageId, StringComparison.Ordinal) &&
            !graph.DependenciesOf(stage.Id).Any());
    }

    /// <summary>
    /// The names an element at this level may depend on, mapped to their ids.
    /// </summary>
    /// <remarks>
    /// Azure matches <c>dependsOn</c> by name and not by id, and the two differ for jobs, whose id
    /// carries their stage so that two stages may each have a job called Test.
    /// </remarks>
    public static Dictionary<string, string> NamesToIds(PipelineModel model, PipelineElementLocation location)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(location);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (location.Kind == PipelineElementLocationKind.Stage)
        {
            foreach (var stage in model.Stages.Where(stage => stage.Name.Length > 0))
            {
                names.TryAdd(stage.Name, stage.Id);
            }
        }
        else
        {
            foreach (var job in location.Stage.Jobs.Where(job => job.Name.Length > 0))
            {
                names.TryAdd(job.Name, job.Id);
            }
        }

        return names;
    }
}
