namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>One <c>job_clusters:</c> entry - the compute a task can bind to by key.</summary>
/// <param name="Key">The <c>job_cluster_key</c>.</param>
/// <param name="SparkVersion">The new cluster's <c>spark_version</c>; empty when unspecified.</param>
/// <param name="NodeType">The new cluster's <c>node_type_id</c>; empty when unspecified.</param>
/// <param name="Workers">The new cluster's <c>num_workers</c>; zero when unspecified.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record JobCluster(string Key, string SparkVersion, string NodeType, int Workers, LineRange Lines);
