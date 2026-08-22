namespace EtAlii.Adp.Backend.Context;

/// <summary>The outcome of resolving one level of a selection.</summary>
public abstract record ContextLevelResolution
{
    public sealed record Resolved(ContextResolvedLevel Level) : ContextLevelResolution;

    public sealed record Rejected(string Reason) : ContextLevelResolution;
}
