namespace EtAlii.Adp.Backend.Context;

/// <summary>The outcome of resolving a whole selection chain; never partial.</summary>
/// <remarks>Either a <see cref="ResolvedChain"/> or a <see cref="RejectedChain"/>.</remarks>
public abstract record ChainResolution;
