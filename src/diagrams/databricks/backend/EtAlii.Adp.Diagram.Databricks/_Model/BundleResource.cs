using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One resource a bundle declares: a job, a pipeline - anything keyed under a kind below
/// <c>resources:</c>.
/// </summary>
/// <param name="Kind">The resource kind as written - <c>jobs</c>, <c>pipelines</c>.</param>
/// <param name="Key">The resource's key under its kind.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record BundleResource(string Kind, string Key, LineRange Lines);
