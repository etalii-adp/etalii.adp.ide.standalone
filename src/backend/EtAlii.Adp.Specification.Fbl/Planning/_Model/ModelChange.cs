namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>
/// One model change, as a DISL transaction makes it and as a conformance fixture's step names it
/// (FBL §6.4, §15.3). Planning turns it into one <see cref="Edit"/> or a refusal.
/// </summary>
public abstract record ModelChange
{
    /// <summary>Adds an element or relation; a relation's ends are the attributes <c>source</c> and <c>target</c>, by id.</summary>
    /// <param name="Index">
    /// Where among <paramref name="ParentId"/>'s children (the top-level elements when it is null) the
    /// element goes: before the one now at this index, after the last when it is past them; null for
    /// last. Only a body whose children are ordered can honour it.
    /// </param>
    /// <param name="Slot">
    /// The containment slot of the parent the element goes in, when the element's type can be
    /// written in several (a view's group order, its hidden groups and its collapsed groups are
    /// one type in three slots). Null chooses the first rule of the type that can insert.
    /// </param>
    public sealed record Add(string Type, string? Id, IReadOnlyDictionary<string, object?> Attributes, string? ParentId = null, int? Index = null, string? Slot = null) : ModelChange;

    /// <summary>
    /// Moves an element, with everything it contains, under <paramref name="NewParentId"/> (to the top
    /// level when it is null), before the child now at <paramref name="Index"/> there, or after the last
    /// when the index is negative or past them.
    /// </summary>
    /// <remarks>
    /// The index counts the new parent's children as they are BEFORE the move, the element itself
    /// included when it is one of them: moving the second of three children to index 0 makes it the
    /// first, to index 3 the last.
    /// </remarks>
    public sealed record Move(string Id, string? NewParentId, int Index) : ModelChange;

    /// <summary>Changes an element's type to <paramref name="Type"/>, with the attributes the new type is given (DISL §9.5 <c>retype</c>).</summary>
    public sealed record Retype(string Id, string Type, IReadOnlyDictionary<string, object?> Attributes) : ModelChange;

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
