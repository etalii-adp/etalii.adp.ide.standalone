namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>
/// One model change, as a DISL transaction makes it and as a conformance fixture's step names it
/// (FBL §6.4, §15.3). Planning turns it into one <see cref="Edit"/> or a refusal.
/// </summary>
public abstract record ModelChange
{
    /// <summary>Adds an element or relation; a relation's ends are the attributes <c>source</c> and <c>target</c>, by id.</summary>
    public sealed record Add(string Type, string? Id, IReadOnlyDictionary<string, object?> Attributes, string? ParentId = null) : ModelChange;

    /// <summary>Sets attributes of one element or relation; an empty string, an empty list or null empties an attribute.</summary>
    public sealed record Set(string Id, IReadOnlyDictionary<string, object?> Attributes) : ModelChange;

    public sealed record Remove(string Id) : ModelChange;

    /// <summary>Places an element on the canvas: an edit of the registration, not of the body (FBL §8.3).</summary>
    public sealed record Place(string Id, double X, double Y) : ModelChange;

    /// <summary>Stores the id of the element with a natural key in the registration's <c>identities</c> (FBL §8.6).</summary>
    public sealed record Identify(string Key, string Id) : ModelChange;

    /// <summary>Saves without a change: no splice (FBL §15.3).</summary>
    public sealed record Save : ModelChange;
}

/// <summary>What planning a change gives: an edit to apply, or the reason it cannot be made (FBL §6.4).</summary>
public abstract record PlanResult
{
    public sealed record Planned(Edit Edit) : PlanResult;

    public sealed record Refused(string Reason) : PlanResult;
}
