namespace EtAlii.Adp.Backend.Context;

/// <summary>A selection chain that was rejected; the current selection stays unchanged.</summary>
public sealed record RejectedChain(string Reason) : ChainResolution;
