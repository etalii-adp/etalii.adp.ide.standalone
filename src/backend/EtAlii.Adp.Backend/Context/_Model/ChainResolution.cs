namespace EtAlii.Adp.Backend.Context;

/// <summary>The outcome of resolving a whole selection chain; never partial.</summary>
public abstract record ChainResolution
{
    public sealed record Resolved(ContextSelectionRecord Record) : ChainResolution;

    public sealed record Rejected(string Reason) : ChainResolution;
}
