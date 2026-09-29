namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What is wrong with a pipeline: a pure function from a parsed model to problems.
/// </summary>
/// <remarks>
/// <para>
/// No file, no canvas, no connection (Requirement 10.9) - which is what lets every rule be tested
/// from a plain YAML string, and is the shape <c>C4RuleSet</c> established.
/// </para>
/// <para>
/// These are the mistakes a YAML file hides, which is most of what anybody would draw this diagram
/// for. A dangling <c>dependsOn</c>, a cycle, a stage nothing can reach and a pipeline with nothing
/// to start with are all invisible while reading the file top to bottom, and all obvious the moment
/// the ordering is drawn.
/// </para>
/// <para>
/// Problems are located on <b>elements</b> rather than on lines. Requirement 10.8 asks for a line
/// "so the panel can take the user to it", and there is nowhere to take them - ADP has no text
/// editor, which is why Requirement 9.9 was descoped. An element location is what the canvas marks
/// (Requirement 8.7) and is therefore the one that reaches the user today; the element's own
/// payload carries its first and last line for whatever eventually opens a file.
/// </para>
/// </remarks>
public static class PipelineRuleSet
{
    /// <summary>Every problem in <paramref name="model"/>, in the order they are worth reading.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(PipelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Stages.Count == 0)
        {
            // A file that declares nothing to run is either an `extends` - whose shape is the
            // template's and not this file's - or one still being written. Neither is a mistake
            // worth reporting, and reporting it would fire on every new pipeline.
            return [];
        }

        var problems = new List<DiagramProblem>();
        var stages = PipelineGraphBuilder.OfStages(model);

        problems.AddRange(Dangling(stages, "stage"));
        problems.AddRange(Cycles(stages, model));
        problems.AddRange(NoStartingStage(stages, model));

        foreach (var stage in model.Stages)
        {
            var jobs = PipelineGraphBuilder.OfJobs(stage);
            problems.AddRange(Dangling(jobs, "job"));
            problems.AddRange(Cycles(jobs, model));
        }

        problems.AddRange(Unreachable(stages, model));
        problems.AddRange(Unnamed(model));
        problems.AddRange(UnfollowableTemplates(model));
        return problems;
    }

    /// <summary>
    /// A <c>dependsOn</c> naming something that is not there (Requirement 10.2).
    /// </summary>
    /// <remarks>
    /// The commonest real mistake in a pipeline file, and the one it is worst at showing: the
    /// name is spelled out plainly a few lines away from the thing it fails to match.
    /// </remarks>
    private static IEnumerable<DiagramProblem> Dangling(PipelineGraph graph, string kind) =>
        graph.BrokenEdges.Select(edge => new DiagramProblem(
            DiagramProblemSeverity.Error,
            $"'{NameOf(edge.ToId)}' waits for a {kind} called '{edge.FromName}', which this pipeline does not have.",
            PipelineRules.DanglingDependency,
            new DiagramProblemElementLocation(edge.ToId)));

    /// <summary>
    /// Elements waiting for each other (Requirement 10.3).
    /// </summary>
    /// <remarks>
    /// Reported once per cycle rather than once per element in it, and naming the whole loop -
    /// "Left waits for Right waits for Left" is actionable where "there is a cycle" is not. The
    /// problem is placed on the first element so the canvas has somewhere to mark it.
    /// </remarks>
    private static IEnumerable<DiagramProblem> Cycles(PipelineGraph graph, PipelineModel model) =>
        graph.Cycles.Select(cycle => new DiagramProblem(
            DiagramProblemSeverity.Error,
            cycle.Count == 1
                ? $"'{NameOf(cycle[0])}' waits for itself, so it can never run."
                : $"These wait for each other and so none of them can run: {string.Join(" -> ", cycle.Select(NameOf))} -> {NameOf(cycle[0])}.",
            PipelineRules.Cycle,
            new DiagramProblemElementLocation(cycle[0])))
        .Where(_ => model.Stages.Count > 0);

    /// <summary>
    /// A pipeline with nothing to begin with (Requirement 10.5).
    /// </summary>
    /// <remarks>
    /// Every stage waiting for another one means the run has no entry point. Located on the file
    /// rather than on an element, because no single stage is the one at fault.
    /// </remarks>
    private static IEnumerable<DiagramProblem> NoStartingStage(PipelineGraph graph, PipelineModel model)
    {
        var starts = model.Stages.Where(stage => !graph.DependenciesOf(stage.Id).Any()).ToList();
        if (starts.Count > 0)
        {
            yield break;
        }

        yield return new DiagramProblem(
            DiagramProblemSeverity.Error,
            "Every stage waits for another one, so this pipeline has nothing to start with. At least one stage must depend on nothing.",
            PipelineRules.NoStartingStage);
    }

    /// <summary>
    /// A stage nothing can reach (Requirement 10.4).
    /// </summary>
    /// <remarks>
    /// Not merely "nothing depends on it" - the last stage of every pipeline is like that, and
    /// reporting it would fire on every correct file. What is reported is a stage that can never
    /// <i>start</i>: everything it waits for is itself unreachable, or is a name that does not
    /// exist. An orphaned stage silently never runs, which is precisely the failure a reader
    /// opened the diagram to find.
    /// </remarks>
    private static IEnumerable<DiagramProblem> Unreachable(PipelineGraph graph, PipelineModel model)
    {
        // Work forwards from the stages that can start, and whatever is not reached cannot run.
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(model.Stages
            .Where(stage => !graph.Edges.Any(edge => edge.ToId == stage.Id))
            .Select(stage => stage.Id));

        foreach (var id in pending)
        {
            reachable.Add(id);
        }

        while (pending.Count > 0)
        {
            foreach (var next in graph.DependentsOf(pending.Dequeue()).Where(reachable.Add))
            {
                pending.Enqueue(next);
            }
        }

        return model.Stages
            .Where(stage => !reachable.Contains(stage.Id))
            // One mistake, one problem. A stage in a cycle, or one whose own dependsOn names
            // something that is not there, already has a problem saying so - and it is the one
            // that names the fix. Adding "and therefore nothing can reach it" is true, is a
            // consequence rather than a cause, and teaches the reader that the panel repeats
            // itself. What survives here is a stage whose cause is genuinely somewhere upstream.
            .Where(stage => !graph.Cycles.Any(cycle => cycle.Contains(stage.Id)))
            .Where(stage => !graph.BrokenEdges.Any(edge => edge.ToId == stage.Id))
            .Select(stage => new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"Nothing can reach '{stage.Label}': what it waits for can never finish, so it will never run.",
                PipelineRules.Unreachable,
                new DiagramProblemElementLocation(stage.Id)));
    }

    /// <summary>
    /// An element with nothing to call it by (Requirement 10.6).
    /// </summary>
    /// <remarks>
    /// A low severity, because the pipeline still runs. It matters because an unnamed stage cannot
    /// be named by anything else's <c>dependsOn</c> and cannot be picked out of a run log - the
    /// cost arrives later, which is exactly when a warning is worth having.
    /// </remarks>
    private static IEnumerable<DiagramProblem> Unnamed(PipelineModel model)
    {
        foreach (var stage in model.Stages.Where(stage => !stage.IsImplicit && Nameless(stage.Name, stage.DisplayName)))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "This stage has no name, so nothing can depend on it and it cannot be picked out of a run log.",
                PipelineRules.Unnamed,
                new DiagramProblemElementLocation(stage.Id));
        }

        foreach (var job in model.Jobs.Where(job => !job.IsImplicit && Nameless(job.Name, job.DisplayName)))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "This job has no name, so nothing can depend on it and it cannot be picked out of a run log.",
                PipelineRules.Unnamed,
                new DiagramProblemElementLocation(job.Id));
        }
    }

    /// <summary>
    /// A template this module can tell it will never follow (Requirement 10.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported as a <b>warning</b> rather than an error, because an unfollowable template is a
    /// limit of the reader and not a defect in the pipeline. The severity enum has two levels and
    /// no informational one - core's deliberate choice, on the grounds that anything informational
    /// is not a problem - and Warning's own definition, "worth attention, but the document still
    /// means something", is exactly what 10.7 describes.
    /// </para>
    /// <para>
    /// Only the references that can be judged from the text alone: one naming another repository,
    /// and one whose path is built from an expression. Whether an ordinary relative path resolves
    /// depends on the filesystem, which a pure function does not have - so it is not guessed at.
    /// </para>
    /// </remarks>
    private static IEnumerable<DiagramProblem> UnfollowableTemplates(PipelineModel model) =>
        model.Templates
            .Where(template => template.Resource.Length > 0 || ContainsExpression(template.Path))
            .Select(template => new DiagramProblem(
                DiagramProblemSeverity.Warning,
                template.Resource.Length > 0
                    ? $"'{template.Reference}' comes from another repository, so what it contributes is not shown here."
                    : $"'{template.Reference}' has a path decided at compile time, so what it contributes is not shown here.",
                PipelineRules.TemplateNotFollowed,
                new DiagramProblemElementLocation($"template:{template.Id}")));

    /// <summary>An element id read back as something to say to a person.</summary>
    /// <remarks>
    /// Ids are paths - <c>Build/Compile</c> - so the last segment is the element's own name, which
    /// is what a reader recognises.
    /// </remarks>
    private static string NameOf(string elementId)
    {
        var slash = elementId.LastIndexOf('/');
        return slash < 0 ? elementId : elementId[(slash + 1)..];
    }

    private static bool Nameless(string name, string displayName) => name.Length == 0 && displayName.Length == 0;

    private static bool ContainsExpression(string path) =>
        path.Contains("${{", StringComparison.Ordinal) ||
        path.Contains("$(", StringComparison.Ordinal) ||
        path.Contains("$[", StringComparison.Ordinal);
}
