namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// What the self-organizing layout produced, or why it declined to produce anything.
/// </summary>
/// <remarks>
/// <para>
/// A refusal is a first-class outcome here rather than an exception or an empty dictionary,
/// because Requirement 6.7 makes the refusal itself something the user must be able to read:
/// "the action is refused for that document with the size at which it fails". A caller that
/// received an empty result could only say that nothing happened.
/// </para>
/// <para>
/// The size is carried separately from the sentence so that a caller can act on the number —
/// a finding, a log line, a threshold — without parsing prose back out of a message.
/// </para>
/// </remarks>
/// <param name="Boxes">Where each variable was placed, empty when the layout was refused.</param>
/// <param name="Refusal">Empty when the layout ran; otherwise why it did not.</param>
/// <param name="Size">The document's variable count, which is the size a refusal is about.</param>
public sealed record SelfOrganizingResult(
    IReadOnlyDictionary<string, CausalLoopBox> Boxes,
    string Refusal,
    int Size)
{
    /// <summary>Whether the layout ran and these positions may be used.</summary>
    public bool IsArranged => Refusal.Length == 0;

    /// <summary>The layout ran.</summary>
    public static SelfOrganizingResult Arranged(IReadOnlyDictionary<string, CausalLoopBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        return new SelfOrganizingResult(boxes, "", boxes.Count);
    }

    /// <summary>
    /// The document is larger than the drawn-element budget, so the layout did not start.
    /// </summary>
    /// <remarks>
    /// Declining before running is the answer to the memory ground (Requirement 6.4). The
    /// sentence names both numbers because a user who is told only "too large" cannot tell
    /// whether they are ten variables over or ten thousand.
    /// </remarks>
    public static SelfOrganizingResult OverBudget(int size, int budget) =>
        new(
            new Dictionary<string, CausalLoopBox>(StringComparer.Ordinal),
            $"This diagram has {size} variables, which is more than the {budget} this layout arranges. It has been left as it is.",
            size);

    /// <summary>
    /// The arrangement could not be separated into non-overlapping boxes.
    /// </summary>
    /// <remarks>
    /// The honest outcome named by Requirement 6.7, and deliberately not a reason to relax the
    /// overlap rule: a diagram delivered as a hairball is worse than a diagram left alone, and
    /// the size is reported so the failure is a fact about this document rather than a shrug.
    /// </remarks>
    public static SelfOrganizingResult CouldNotSeparate(int size, int overlaps) =>
        new(
            new Dictionary<string, CausalLoopBox>(StringComparer.Ordinal),
            $"This layout could not arrange {size} variables without {overlaps} of them overlapping, so the diagram has been left as it is.",
            size);
}
