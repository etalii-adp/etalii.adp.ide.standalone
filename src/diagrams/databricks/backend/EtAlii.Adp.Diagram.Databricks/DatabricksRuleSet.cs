namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The family's rules: a pure function from a parsed entry to the problems in it, each with the
/// line the models recorded and a sentence saying what to do (databricks-diagrams Requirement 12).
/// </summary>
/// <remarks>
/// Nothing here looks at a workspace, the disk or another file (Requirement 12.3) - which is why
/// the override rule stays quiet for a bundle with <c>include:</c> globs: a resource the override
/// names may be declared in an included file this rule set cannot see, and a rule that cannot
/// know must not accuse.
/// </remarks>
internal static class DatabricksRuleSet
{
    public static IReadOnlyList<DiagramProblem> Judge(DatabricksDocumentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var problems = new List<DiagramProblem>();
        foreach (var job in entry.Jobs)
        {
            JudgeJob(job, problems);
        }

        JudgeBundle(entry.Bundle, problems);
        foreach (var pipeline in entry.Pipelines)
        {
            JudgePipeline(pipeline, problems);
        }

        return problems;
    }

    private static void JudgeJob(JobModel job, List<DiagramProblem> problems)
    {
        var keys = job.Tasks.Select(task => task.Key).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);

        foreach (var task in job.Tasks)
        {
            if (task.Key.Length == 0)
            {
                problems.Add(Error(
                    "A task has no task_key. Give it one, or nothing can depend on it.",
                    "databricks.task-key-missing", task.Lines));
            }

            foreach (var dependency in task.DependsOn.Where(dependency => !keys.Contains(dependency.TaskKey)))
            {
                problems.Add(Error(
                    $"'{task.Key}' depends on '{dependency.TaskKey}', which is not a task in this job. Fix the key or remove the dependency.",
                    "databricks.missing-task", dependency.Lines));
            }

            if (task.ClusterKey.Length > 0 && job.Clusters.All(cluster => cluster.Key != task.ClusterKey))
            {
                problems.Add(Error(
                    $"'{task.Key}' runs on cluster '{task.ClusterKey}', which this job does not declare. Declare it under job_clusters, or clear the binding for serverless.",
                    "databricks.dangling-cluster", task.Lines));
            }
        }

        foreach (var duplicates in job.Tasks
            .Where(task => task.Key.Length > 0)
            .GroupBy(task => task.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (var task in duplicates.Skip(1))
            {
                problems.Add(Error(
                    $"The task key '{duplicates.Key}' is used more than once. Every task needs its own.",
                    "databricks.duplicate-task-key", task.Lines));
            }
        }

        foreach (var member in CycleMembers(job))
        {
            problems.Add(Error(
                $"'{member.Key}' is part of a dependency circle, so it can never run. Break the circle.",
                "databricks.cycle", member.Lines));
        }
    }

    private static void JudgeBundle(BundleModel bundle, List<DiagramProblem> problems)
    {
        var defaults = bundle.Targets.Where(target => target.IsDefault).ToList();
        if (bundle.Targets.Count > 0 && defaults.Count == 0)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "No target says default: true, so every deploy must name one. Mark one target as the default.",
                "databricks.no-default-target",
                new DiagramProblemLineLocation((uint)(bundle.Targets[0].Lines.Start + 1))));
        }

        foreach (var target in defaults.Skip(1))
        {
            problems.Add(Error(
                $"'{target.Name}' is a second default target; only one target may be the default.",
                "databricks.multiple-default-targets", target.Lines));
        }

        // With include: globs in play, an override may name a resource declared in an included
        // file this rule set cannot see - so the rule only speaks when the bundle stands alone.
        if (bundle.Includes.Count == 0)
        {
            foreach (var target in bundle.Targets)
            {
                foreach (var stray in target.Overrides.Where(overridden =>
                    !bundle.Resources.Any(resource =>
                        resource.Kind == overridden.Kind && resource.Key == overridden.Key)))
                {
                    problems.Add(new DiagramProblem(
                        DiagramProblemSeverity.Warning,
                        $"Target '{target.Name}' overrides {stray.Kind} '{stray.Key}', which this bundle does not declare. Declare the resource or drop the override.",
                        "databricks.override-of-undeclared",
                        new DiagramProblemLineLocation((uint)(stray.Lines.Start + 1))));
                }
            }
        }
    }

    private static void JudgePipeline(PipelineModel pipeline, List<DiagramProblem> problems)
    {
        if (pipeline.Libraries.Count == 0)
        {
            problems.Add(Error(
                $"The pipeline{Named(pipeline)} has no libraries, so it has nothing to run. Add at least one notebook, file or glob.",
                "databricks.no-libraries", pipeline.Lines));
        }

        if (pipeline.Schema.Length > 0 && pipeline.Catalog.Length == 0)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The pipeline{Named(pipeline)} sets a schema but no catalog, so the schema has nowhere to live. Set catalog too.",
                "databricks.schema-without-catalog",
                new DiagramProblemLineLocation((uint)(pipeline.Lines.Start + 1))));
        }
    }

    private static string Named(PipelineModel pipeline) =>
        pipeline.Name.Length > 0 ? $" '{pipeline.Name}'" : "";

    /// <summary>
    /// The tasks on a dependency circle - found by repeatedly removing tasks with no remaining
    /// unresolved dependencies; whatever cannot be removed is circular. Dependencies on tasks
    /// that do not exist are someone else's finding and do not hold a task back here.
    /// </summary>
    private static IEnumerable<JobTask> CycleMembers(JobModel job)
    {
        // TryAdd rather than ToDictionary: a duplicated task key is its own finding, and must
        // not crash the cycle rule that happens to run after it.
        var remaining = new Dictionary<string, JobTask>(StringComparer.Ordinal);
        foreach (var task in job.Tasks)
        {
            remaining.TryAdd(task.Key, task);
        }

        var removed = true;
        while (removed)
        {
            removed = false;
            foreach (var (key, task) in remaining.ToList())
            {
                if (task.DependsOn.All(dependency => !remaining.ContainsKey(dependency.TaskKey)))
                {
                    remaining.Remove(key);
                    removed = true;
                }
            }
        }

        return remaining.Values.OrderBy(task => task.Lines.Start);
    }

    private static DiagramProblem Error(string message, string ruleId, LineRange lines) =>
        new(DiagramProblemSeverity.Error, message, ruleId, new DiagramProblemLineLocation((uint)(lines.Start + 1)));
}
