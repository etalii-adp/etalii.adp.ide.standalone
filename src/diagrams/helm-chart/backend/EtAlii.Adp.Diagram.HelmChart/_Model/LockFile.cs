namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>The <c>Chart.lock</c> (or legacy <c>requirements.lock</c>): what <c>helm dependency update</c> pinned.</summary>
/// <param name="RelativePath">Chart-root-relative.</param>
/// <param name="Entries">The pinned dependencies, ordinally by name.</param>
/// <param name="Failure">Why the lock did not parse, when it did not.</param>
public sealed record LockFile(string RelativePath, IReadOnlyList<LockEntry> Entries, HelmYamlFailure? Failure);
