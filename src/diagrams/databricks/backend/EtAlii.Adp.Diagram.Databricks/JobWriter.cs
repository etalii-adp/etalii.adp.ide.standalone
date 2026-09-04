using System.Globalization;
using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The named splice operations on a job: tasks in and out, edges connected and disconnected,
/// keys renamed with every reference in one operation - only the lines an edit concerns change
/// (databricks-diagrams Requirements 2.2, 6 and 11.4).
/// </summary>
/// <remarks>
/// Every operation answers with an empty string, or the sentence explaining why it refused - and
/// a refusal happens <b>before</b> any splice, never write-then-repair, so a refused document is
/// untouched by construction (Requirement 6.4). A successful splice invalidates the model that
/// located it; callers re-parse.
/// </remarks>
internal static class JobWriter
{
    /// <summary>The skeleton each insertable task type opens with: its <c>*_task</c> key and starter fields.</summary>
    private static readonly Dictionary<string, string[]> _skeletons = new(StringComparer.Ordinal)
    {
        ["notebook"] = ["notebook_task:", "  notebook_path: {0}"],
        ["python"] = ["spark_python_task:", "  python_file: {0}"],
        ["wheel"] = ["python_wheel_task:", "  package_name: {0}", "  entry_point: main"],
        ["sql"] = ["sql_task:", "  warehouse_id: \"\"", "  query:", "    query_id: {0}"],
        ["pipeline"] = ["pipeline_task:", "  pipeline_id: {0}"],
        ["run-job"] = ["run_job_task:", "  job_id: {0}"],
        ["condition"] = ["condition_task:", "  op: EQUAL_TO", "  left: \"{0}\"", "  right: \"true\""],
    };

    /// <summary>Inserts a new task after the job's last one.</summary>
    public static string InsertTask(
        DatabricksDocument document, JobModel job, string taskKey, string taskType, string source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(job);

        if (taskKey.Length == 0)
        {
            return "A task needs a key.";
        }

        if (job.Tasks.Any(task => task.Key == taskKey))
        {
            return $"A task named '{taskKey}' is already there.";
        }

        if (!_skeletons.TryGetValue(taskType, out var skeleton))
        {
            return $"There is no '{taskType}' task type to add.";
        }

        var (itemIndent, keyIndent, insertAt) = TaskInsertion(document, job);
        if (insertAt < 0)
        {
            return "The job has no tasks section to add into.";
        }

        var lines = new List<string> { $"{itemIndent}- task_key: {taskKey}" };
        lines.AddRange(skeleton.Select(part =>
            keyIndent + string.Format(CultureInfo.InvariantCulture, part, DatabricksSplices.Quote(source))));
        document.Insert(insertAt, lines);
        return "";
    }

    /// <summary>
    /// Removes a task and every other task's <c>depends_on</c> reference to it, in one splice
    /// each, from the bottom of the document upwards.
    /// </summary>
    /// <remarks>
    /// Descending order matters: removing a range shifts every line after it, so removing
    /// top-down would leave every later range pointing at the wrong lines. The references go with
    /// the task because an edge to something that no longer exists is not a diagram anybody
    /// wants; <see cref="ReferencesTo"/> answers how many, before anything runs.
    /// </remarks>
    public static string RemoveTask(DatabricksDocument document, JobModel job, string taskKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(job);

        var task = job.Tasks.FirstOrDefault(candidate => candidate.Key == taskKey);
        if (task is null)
        {
            return $"There is no task named '{taskKey}' any more.";
        }

        var ranges = new List<LineRange> { task.Lines };
        foreach (var other in job.Tasks.Where(candidate => candidate.Key != taskKey))
        {
            ranges.AddRange(DependencyRemovals(document, other, other.DependsOn
                .Where(dependency => dependency.TaskKey == taskKey)
                .ToList()));
        }

        foreach (var range in ranges.OrderByDescending(range => range.Start))
        {
            document.Remove(range);
        }

        return "";
    }

    /// <summary>The dependencies that would go with a task, so an action can say how many before it runs.</summary>
    public static IReadOnlyList<JobDependency> ReferencesTo(JobModel job, string taskKey)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Tasks
            .Where(task => task.Key != taskKey)
            .SelectMany(task => task.DependsOn)
            .Where(dependency => dependency.TaskKey == taskKey)
            .ToList();
    }

    /// <summary>Adds a <c>depends_on</c> entry: <paramref name="toKey"/> comes to depend on <paramref name="fromKey"/>.</summary>
    public static string Connect(
        DatabricksDocument document, JobModel job, string fromKey, string toKey, string outcome = "")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(job);

        if (fromKey == toKey)
        {
            return "A task cannot depend on itself.";
        }

        var to = job.Tasks.FirstOrDefault(task => task.Key == toKey);
        if (to is null || job.Tasks.All(task => task.Key != fromKey))
        {
            return "One of those tasks is not there any more.";
        }

        if (to.DependsOn.Any(dependency => dependency.TaskKey == fromKey))
        {
            return $"'{toKey}' already depends on '{fromKey}'.";
        }

        var keyIndent = DatabricksSplices.KeyIndentWithin(document, to.Lines);
        var entryIndent = to.DependsOn.Count > 0
            ? DatabricksSplices.Indent(document.Lines[to.DependsOn[^1].Lines.Start].Text)
            : keyIndent + "  ";

        var entry = new List<string> { $"{entryIndent}- task_key: {fromKey}" };
        if (outcome.Length > 0)
        {
            entry.Add($"{entryIndent}  outcome: \"{outcome}\"");
        }

        if (to.DependsOn.Count > 0)
        {
            document.Insert(to.DependsOn[^1].Lines.End + 1, entry);
            return "";
        }

        var dependsOnLine = DatabricksSplices.FindKey(document, to.Lines, "depends_on");
        if (dependsOnLine >= 0)
        {
            document.Insert(dependsOnLine + 1, entry);
            return "";
        }

        // No depends_on yet: the key and its first entry go directly under the task_key line.
        document.Insert(to.Lines.Start + 1, [$"{keyIndent}depends_on:", .. entry]);
        return "";
    }

    /// <summary>
    /// Removes one <c>depends_on</c> entry - and the <c>depends_on:</c> key itself when that was
    /// the last one, so an undo of the connect that created both restores the file byte for byte.
    /// </summary>
    public static string Disconnect(DatabricksDocument document, JobModel job, string fromKey, string toKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(job);

        var to = job.Tasks.FirstOrDefault(task => task.Key == toKey);
        var dependency = to?.DependsOn.FirstOrDefault(candidate => candidate.TaskKey == fromKey);
        if (to is null || dependency is null)
        {
            return $"There is no dependency from '{fromKey}' to '{toKey}' any more.";
        }

        foreach (var range in DependencyRemovals(document, to, [dependency])
            .OrderByDescending(range => range.Start))
        {
            document.Remove(range);
        }

        return "";
    }

    /// <summary>
    /// Renames a task, rewriting its own <c>task_key</c> and every <c>depends_on</c> reference to
    /// it in the same operation - so no reference is ever left stranded (Requirement 11.4).
    /// </summary>
    public static string RenameTask(DatabricksDocument document, JobModel job, string taskKey, string newKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(job);

        if (newKey.Length == 0)
        {
            return "A task needs a key.";
        }

        var task = job.Tasks.FirstOrDefault(candidate => candidate.Key == taskKey);
        if (task is null)
        {
            return $"There is no task named '{taskKey}' any more.";
        }

        if (newKey != taskKey && job.Tasks.Any(candidate => candidate.Key == newKey))
        {
            return $"A task named '{newKey}' is already there.";
        }

        var rewrites = new List<int>();
        var own = DatabricksSplices.FindKey(document, task.Lines, "task_key");
        if (own < 0)
        {
            return $"The task '{taskKey}' has no task_key line to rewrite.";
        }

        rewrites.Add(own);
        rewrites.AddRange(job.Tasks
            .SelectMany(candidate => candidate.DependsOn)
            .Where(dependency => dependency.TaskKey == taskKey)
            .Select(dependency => DatabricksSplices.FindKey(document, dependency.Lines, "task_key"))
            .Where(line => line >= 0));

        foreach (var line in rewrites.OrderByDescending(line => line))
        {
            var text = document.Lines[line].Text;
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            document.Replace(new LineRange(line, line), [$"{text[..(colon + 1)]} {newKey}"]);
        }

        return "";
    }

    /// <summary>Rewrites a task's <c>run_if</c>; an empty value removes the key, restoring the default.</summary>
    public static string SetRunIf(DatabricksDocument document, JobModel job, string taskKey, string runIf) =>
        SetTaskKey(document, job, taskKey, "run_if", runIf);

    /// <summary>Rewrites a task's cluster binding; an empty value removes the key - serverless.</summary>
    public static string SetCluster(DatabricksDocument document, JobModel job, string taskKey, string clusterKey)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (clusterKey.Length > 0 && job.Clusters.All(cluster => cluster.Key != clusterKey))
        {
            return $"There is no cluster named '{clusterKey}' in this job.";
        }

        return SetTaskKey(document, job, taskKey, "job_cluster_key", clusterKey);
    }

    private static string SetTaskKey(
        DatabricksDocument document, JobModel job, string taskKey, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(job);

        var task = job.Tasks.FirstOrDefault(candidate => candidate.Key == taskKey);
        if (task is null)
        {
            return $"There is no task named '{taskKey}' any more.";
        }

        var line = DatabricksSplices.FindKey(document, task.Lines, key);
        if (value.Length == 0)
        {
            if (line >= 0)
            {
                document.Remove(new LineRange(line, line));
            }

            return "";
        }

        if (line >= 0)
        {
            var text = document.Lines[line].Text;
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            document.Replace(new LineRange(line, line), [$"{text[..(colon + 1)]} {value}"]);
            return "";
        }

        var keyIndent = DatabricksSplices.KeyIndentWithin(document, task.Lines);
        document.Insert(task.Lines.Start + 1, [$"{keyIndent}{key}: {value}"]);
        return "";
    }

    /// <summary>
    /// What removing some of a task's dependencies splices: each entry's lines - plus the
    /// <c>depends_on:</c> key line itself when none would remain.
    /// </summary>
    private static List<LineRange> DependencyRemovals(
        DatabricksDocument document, JobTask task, IReadOnlyList<JobDependency> removed)
    {
        var ranges = removed.Select(dependency => dependency.Lines).ToList();
        if (removed.Count > 0 && removed.Count == task.DependsOn.Count)
        {
            var keyLine = DatabricksSplices.FindKey(document, task.Lines, "depends_on");
            if (keyLine >= 0)
            {
                ranges.Add(new LineRange(keyLine, keyLine));
            }
        }

        return ranges;
    }

    /// <summary>
    /// Where a new task goes and how it is indented: after the last task, copying its style, or
    /// directly under a bare <c>tasks:</c> key. -1 when there is nowhere to insert.
    /// </summary>
    private static (string ItemIndent, string KeyIndent, int InsertAt) TaskInsertion(
        DatabricksDocument document, JobModel job)
    {
        if (job.Tasks.Count > 0)
        {
            var last = job.Tasks[^1];
            var itemIndent = DatabricksSplices.Indent(document.Lines[last.Lines.Start].Text);
            return (itemIndent, DatabricksSplices.KeyIndentWithin(document, last.Lines), last.Lines.End + 1);
        }

        var tasksLine = DatabricksSplices.FindKey(document, job.Lines, "tasks");
        if (tasksLine < 0)
        {
            return ("", "", -1);
        }

        var keyIndent = DatabricksSplices.Indent(document.Lines[tasksLine].Text);
        return (keyIndent + "  ", keyIndent + "    ", tasksLine + 1);
    }
}
