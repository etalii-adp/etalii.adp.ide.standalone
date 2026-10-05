namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>What <c>plan</c> delivers: splices, or the sentence the host shows when the change is refused.</summary>
public abstract record PluginPlanResult
{
    public sealed record Planned(IReadOnlyList<PluginSplice> Splices) : PluginPlanResult;

    public sealed record Refused(string Reason) : PluginPlanResult;
}
