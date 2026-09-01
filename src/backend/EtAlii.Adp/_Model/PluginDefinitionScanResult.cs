namespace EtAlii.Adp;

/// <summary>
/// What one <see cref="PluginDefinitionScan"/> pass over a set of assemblies produced: every
/// hit in scan order, and how many assemblies were looked at - which the families' own summary
/// log lines report.
/// </summary>
internal sealed record PluginDefinitionScanResult<T>(IReadOnlyList<PluginDefinitionHit<T>> Found, int AssembliesScanned);
