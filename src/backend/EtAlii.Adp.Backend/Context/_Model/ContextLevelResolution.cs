namespace EtAlii.Adp.Backend.Context;

/// <summary>The outcome of resolving one level of a selection.</summary>
/// <remarks>Either a <see cref="ResolvedContextLevel"/> or a <see cref="RejectedContextLevel"/>.</remarks>
public abstract record ContextLevelResolution;
