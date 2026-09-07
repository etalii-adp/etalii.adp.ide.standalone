using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One task of a job: what it runs, what it waits for, and where.
/// </summary>
/// <param name="Key">The <c>task_key</c>.</param>
/// <param name="Type">
/// The task's type, derived from which <c>*_task</c> mapping it carries - <c>notebook</c>,
/// <c>python</c>, <c>wheel</c>, <c>sql</c>, <c>dbt</c>, <c>pipeline</c>, <c>run-job</c>,
/// <c>condition</c>, <c>for-each</c>, <c>jar</c> - or <c>other</c> for one this module does not
/// recognise, which still draws as a box.
/// </param>
/// <param name="Source">The type's principal source in display form - a notebook path, a python file, a query id.</param>
/// <param name="ClusterKey">The <c>job_cluster_key</c> the task binds to; empty means serverless.</param>
/// <param name="RunIf">The <c>run_if</c> as written; empty means the default <c>ALL_SUCCESS</c>.</param>
/// <param name="DependsOn">The <c>depends_on</c> entries, in file order.</param>
/// <param name="Lines">The lines that declare the task.</param>
public sealed record JobTask(
    string Key,
    string Type,
    string Source,
    string ClusterKey,
    string RunIf,
    IReadOnlyList<JobDependency> DependsOn,
    LineRange Lines);
