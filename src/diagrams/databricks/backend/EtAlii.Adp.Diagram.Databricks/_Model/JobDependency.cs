namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One <c>depends_on</c> entry: the upstream task, and - after a condition task - which of its
/// outcomes this edge follows.
/// </summary>
/// <param name="TaskKey">The upstream task's key.</param>
/// <param name="Outcome">The <c>outcome:</c> as written - <c>"true"</c>, <c>"false"</c> - or empty for an ordinary edge.</param>
/// <param name="Lines">The lines that declare the entry - what disconnecting this one edge splices.</param>
public sealed record JobDependency(string TaskKey, string Outcome, LineRange Lines);
