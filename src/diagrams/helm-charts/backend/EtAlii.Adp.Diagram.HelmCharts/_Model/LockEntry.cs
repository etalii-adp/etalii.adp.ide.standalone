namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>One pinned dependency in the lock.</summary>
/// <param name="Name">The dependency name as the lock records it.</param>
/// <param name="Version">The exact pinned version - displayed beside the declared constraint, never evaluated against it.</param>
/// <param name="Line">The 1-based line the entry starts on.</param>
public sealed record LockEntry(string Name, string? Version, uint Line);
