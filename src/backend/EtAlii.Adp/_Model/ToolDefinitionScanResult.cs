namespace EtAlii.Adp;

/// <summary>
/// What one <see cref="ToolDefinitionScan"/> pass over a set of assemblies produced: every
/// hit in scan order, and how many assemblies were looked at - which the families' own summary
/// log lines report.
/// </summary>
internal sealed record ToolDefinitionScanResult<T>(IReadOnlyList<ToolDefinitionHit<T>> Found, int AssembliesScanned);
