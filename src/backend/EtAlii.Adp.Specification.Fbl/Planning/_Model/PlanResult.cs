namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>What planning a change gives: an edit to apply, or the reason it cannot be made (FBL §6.4).</summary>
public abstract record PlanResult
{
    public sealed record Planned(Edit Edit) : PlanResult;

    public sealed record Refused(string Reason) : PlanResult;
}
