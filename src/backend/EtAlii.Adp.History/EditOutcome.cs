namespace EtAlii.Adp.History;

/// <summary>What came of one edit of a document's body: it was spliced in, or it was refused and why.</summary>
/// <remarks>
/// One type for every module, where six declared their own (code-structure FR-033). It says nothing
/// of the document it was applied to: the edit changed its body in place, and the run that asked for
/// it saves that body.
/// </remarks>
/// <param name="Refusal">Why the edit was refused, for the user; null when it was applied.</param>
public readonly record struct EditOutcome(string? Refusal)
{
    /// <summary>The edit was spliced into the body.</summary>
    public static EditOutcome Applied { get; } = new(null);

    /// <summary>The edit was refused, and this is why.</summary>
    public static EditOutcome Refused(string because) => new(because);

    /// <summary>Whether the body changed.</summary>
    public bool WasApplied => Refusal is null;
}
