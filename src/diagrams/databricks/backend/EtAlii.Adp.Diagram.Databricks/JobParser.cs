using EtAlii.Adp.Documents;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Reads every Lakeflow job a document declares under <c>resources: jobs:</c>, recording which
/// lines declare what - the task DAG the job diagram draws (Requirement 4).
/// </summary>
/// <remarks>
/// Forgiving on purpose. A task without a key, a dependency naming a task that is not there, a
/// task type this module does not recognise - none of these throw, because the rest of the
/// diagram still draws and the problems are the validator's to report (Requirement 12.2).
/// </remarks>
internal static class JobParser
{
    /// <summary>
    /// The <c>*_task</c> mappings that decide a task's type, and the field within each that is
    /// its principal source. Order matters only for the pathological task carrying several.
    /// </summary>
    private static readonly (string Key, string Type, string SourceField)[] _taskKinds =
    [
        ("notebook_task", "notebook", "notebook_path"),
        ("spark_python_task", "python", "python_file"),
        ("python_wheel_task", "wheel", "entry_point"),
        ("sql_task", "sql", ""),
        ("dbt_task", "dbt", "project_directory"),
        ("pipeline_task", "pipeline", "pipeline_id"),
        ("run_job_task", "run-job", "job_id"),
        ("condition_task", "condition", "op"),
        ("for_each_task", "for-each", "inputs"),
        ("spark_jar_task", "jar", "main_class_name"),
    ];

    public static IReadOnlyList<JobModel> Parse(YamlMappingNode? root, LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var jobs = new List<JobModel>();
        if (root is null || YamlAccess.Mapping(root, "resources") is not { } resources
            || YamlAccess.Mapping(resources, "jobs") is not { } keyed)
        {
            return jobs;
        }

        foreach ((YamlNode keyNode, YamlNode body) in keyed.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key } || body is not YamlMappingNode job)
            {
                continue;
            }

            var schedule = YamlAccess.Mapping(job, "schedule");
            jobs.Add(new JobModel(
                key,
                YamlAccess.Scalar(job, "name") ?? "",
                ReadTasks(job, document),
                ReadClusters(job, document),
                schedule is null ? "" : YamlAccess.Scalar(schedule, "quartz_cron_expression") ?? "",
                job.Children.ContainsKey(new YamlScalarNode("continuous")),
                YamlAccess.Range(keyNode, body, document)));
        }

        return jobs;
    }

    private static List<JobTask> ReadTasks(YamlMappingNode job, LineDocument document)
    {
        var tasks = new List<JobTask>();
        foreach (var node in YamlAccess.Sequence(job, "tasks"))
        {
            if (node is not YamlMappingNode task)
            {
                continue;
            }

            (string type, string source) = Kind(task);
            tasks.Add(new JobTask(
                YamlAccess.Scalar(task, "task_key") ?? "",
                type,
                source,
                YamlAccess.Scalar(task, "job_cluster_key") ?? "",
                YamlAccess.Scalar(task, "run_if") ?? "",
                ReadDependencies(task, document),
                YamlAccess.Range(task, document)));
        }

        return tasks;
    }

    /// <summary>
    /// A task's type is whichever <c>*_task</c> mapping it carries; one carrying none is
    /// <c>other</c>, which still draws as a box rather than vanishing.
    /// </summary>
    private static (string Type, string Source) Kind(YamlMappingNode task)
    {
        foreach ((string key, string type, string sourceField) in _taskKinds)
        {
            if (YamlAccess.Mapping(task, key) is not { } declaration)
            {
                continue;
            }

            var source = sourceField.Length > 0 ? YamlAccess.Scalar(declaration, sourceField) ?? "" : "";
            if (type == "sql")
            {
                // The SQL task's source sits one level deeper, and which key is there depends on
                // what it runs: a saved query, a dashboard, an alert or a file.
                source = SqlSource(declaration);
            }

            return (type, source);
        }

        return ("other", "");
    }

    private static string SqlSource(YamlMappingNode declaration)
    {
        foreach (var kind in (string[])["query", "dashboard", "alert", "file"])
        {
            if (YamlAccess.Mapping(declaration, kind) is { } inner)
            {
                return YamlAccess.Scalar(inner, $"{kind}_id")
                    ?? YamlAccess.Scalar(inner, "path")
                    ?? "";
            }
        }

        return "";
    }

    private static List<JobDependency> ReadDependencies(YamlMappingNode task, LineDocument document)
    {
        var dependencies = new List<JobDependency>();
        foreach (var node in YamlAccess.Sequence(task, "depends_on"))
        {
            if (node is YamlMappingNode dependency
                && YamlAccess.Scalar(dependency, "task_key") is { Length: > 0 } upstream)
            {
                dependencies.Add(new JobDependency(
                    upstream,
                    YamlAccess.Scalar(dependency, "outcome") ?? "",
                    YamlAccess.Range(dependency, document)));
            }
        }

        return dependencies;
    }

    private static List<JobCluster> ReadClusters(YamlMappingNode job, LineDocument document)
    {
        var clusters = new List<JobCluster>();
        foreach (var node in YamlAccess.Sequence(job, "job_clusters"))
        {
            if (node is not YamlMappingNode cluster
                || YamlAccess.Scalar(cluster, "job_cluster_key") is not { Length: > 0 } key)
            {
                continue;
            }

            var spec = YamlAccess.Mapping(cluster, "new_cluster");
            clusters.Add(new JobCluster(
                key,
                spec is null ? "" : YamlAccess.Scalar(spec, "spark_version") ?? "",
                spec is null ? "" : YamlAccess.Scalar(spec, "node_type_id") ?? "",
                spec is not null && int.TryParse(YamlAccess.Scalar(spec, "num_workers"), out var workers)
                    ? workers
                    : 0,
                YamlAccess.Range(cluster, document)));
        }

        return clusters;
    }
}
