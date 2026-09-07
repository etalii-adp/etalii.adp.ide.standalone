namespace EtAlii.Adp.Context;

/// <summary>A selection chain that resolved end to end, with the record it produced.</summary>
public sealed record ResolvedChain(ContextSelectionRecord Record) : ChainResolution;
