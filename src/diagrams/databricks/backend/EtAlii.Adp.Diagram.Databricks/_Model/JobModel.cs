using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One Lakeflow job as a resource file declares it: its tasks and their dependencies - the DAG
/// the job diagram draws (Requirement 4) - plus the clusters and trigger the tasks refer to.
/// </summary>
/// <param name="Key">The job's key under <c>resources: jobs:</c>; empty for a file that declares none.</param>
/// <param name="Name">The job's display <c>name:</c>; empty when it has none.</param>
/// <param name="Tasks">The tasks, in file order.</param>
/// <param name="Clusters">The declared <c>job_clusters:</c> entries.</param>
/// <param name="Schedule">The <c>schedule:</c>'s cron expression as written; empty when there is none.</param>
/// <param name="Continuous">Whether the job declares <c>continuous:</c>.</param>
/// <param name="Lines">The lines that declare the job.</param>
public sealed record JobModel(
    string Key,
    string Name,
    IReadOnlyList<JobTask> Tasks,
    IReadOnlyList<JobCluster> Clusters,
    string Schedule,
    bool Continuous,
    LineRange Lines);
