namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>One relation's rule: who may be its source, who may be its target, and how many.</summary>
/// <param name="Id">The relation id, which is also the connection's <c>type</c> in the document.</param>
/// <param name="Sources">The element types allowed as the source. Order is the requirement's.</param>
/// <param name="Target">The one element type allowed as the target.</param>
/// <param name="MaxIntoTarget">At most this many of this relation may arrive at one target, or <c>null</c> for no limit.</param>
/// <param name="MaxFromSource">At most this many of this relation may leave one source, or <c>null</c> for no limit.</param>
/// <param name="IsOwnership">Whether this relation is one of the four a cycle is made of.</param>
public sealed record FdgRelation(
    string Id,
    IReadOnlyList<string> Sources,
    string Target,
    int? MaxIntoTarget,
    int? MaxFromSource,
    bool IsOwnership);

/// <summary>
/// Requirement 5's table, as data, stated once on the backend.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one backend statement of the table.</b> The rule set reads it and so does the connect
/// command's refusal, so a link the canvas would not offer cannot be written by a stale or
/// scripted request either - and the two can never drift, because there is only one table. The
/// client states the same table as declarations; until <c>backend-centralization</c> R12's
/// type-string fixture exists, a module test asserts these ids against a checked-in copy of the
/// client's.
/// </para>
/// <para>
/// <b>Every arrow points from parent to child.</b> `Shows` is the exception that proves it matters:
/// its source is an Action and its target a UI Element, which is the direction a reader expects,
/// but it is <b>not</b> ownership - so it is exempt from the cycle rule, and a navigation loop
/// (a task list, a task detail, back to the task list) is legal and must stay drawable.
/// </para>
/// <para>
/// <b>The limits are per relation rather than per element</b>, which is what makes "one parent in
/// total" true for a Data Element and a Function: only one relation targets each of them, so a
/// <see cref="FdgRelation.MaxIntoTarget"/> of 1 on that relation is the whole of the limit. Were a
/// second relation ever to target the same type, this table would need a per-target limit instead,
/// and that is the change to make rather than adding a special case elsewhere.
/// </para>
/// </remarks>
public static class FdgRelations
{
    /// <summary>The five relations, in the order Requirement 5's table lists them.</summary>
    public static readonly IReadOnlyList<FdgRelation> All =
    [
        new(
            FdgConnectionTypes.UiChild,
            [FdgElementTypes.UiElement],
            FdgElementTypes.UiElement,
            MaxIntoTarget: 1,
            MaxFromSource: null,
            IsOwnership: true),
        new(
            FdgConnectionTypes.OwnsAction,
            [FdgElementTypes.UiElement],
            FdgElementTypes.Action,
            MaxIntoTarget: 1,
            MaxFromSource: null,
            IsOwnership: true),
        new(
            FdgConnectionTypes.OwnsData,
            [FdgElementTypes.UiElement, FdgElementTypes.Action, FdgElementTypes.DataElement],
            FdgElementTypes.DataElement,
            MaxIntoTarget: 1,
            MaxFromSource: null,
            IsOwnership: true),
        new(
            FdgConnectionTypes.OwnsFunction,
            [FdgElementTypes.UiElement, FdgElementTypes.Action, FdgElementTypes.DataElement, FdgElementTypes.Function],
            FdgElementTypes.Function,
            MaxIntoTarget: 1,
            MaxFromSource: null,
            IsOwnership: true),
        new(
            FdgConnectionTypes.Shows,
            [FdgElementTypes.Action],
            FdgElementTypes.UiElement,
            MaxIntoTarget: null,
            MaxFromSource: 1,
            IsOwnership: false),
    ];

    /// <summary>The relation with this id, or <c>null</c> when the document names one that does not exist.</summary>
    public static FdgRelation? ById(string id) =>
        All.FirstOrDefault(relation => string.Equals(relation.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Whether this relation admits a link between these two element types.
    /// </summary>
    /// <remarks>
    /// <b>A Comment is refused by construction rather than by a rule of its own</b>: it appears in
    /// no relation's sources and is no relation's target, so every link to or from one fails this
    /// check. That is why Requirement 5.3 can say a Comment has no anchors without the rule set
    /// needing a Comment clause.
    /// </remarks>
    public static bool Admits(this FdgRelation relation, string sourceType, string targetType)
    {
        ArgumentNullException.ThrowIfNull(relation);

        return relation.Sources.Contains(sourceType, StringComparer.Ordinal)
            && string.Equals(relation.Target, targetType, StringComparison.Ordinal);
    }
}
