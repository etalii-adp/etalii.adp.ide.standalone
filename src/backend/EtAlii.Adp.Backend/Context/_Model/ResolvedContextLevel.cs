namespace EtAlii.Adp.Backend.Context;

/// <summary>One level of a selection that resolved to a real, authorized target.</summary>
public sealed record ResolvedContextLevel(ContextResolvedLevel Level) : ContextLevelResolution;
