namespace EtAlii.Adp.History;

/// <summary>What came of one edit of a document's body: it was spliced in, or it was refused and why.</summary>
/// <remarks>
/// One type for every module, where six declared their own (code-structure FR-033). It says nothing
/// of the document it was applied to: the edit changed its body in place, and the run that asked for
/// it saves that body.
/// </remarks>
/// <param name="Refusal">Why the edit was refused, for the user; null when it was applied.</param>
/// <param name="Inverse">
/// The command that undoes the edit, where the module names one; null leaves the undo to the run,
/// which restores the whole text. <b>A module names its own</b> where its undo must outlive a later
/// write of the file, as a rename back to the old label does and a restore of the old text cannot.
/// </param>
public readonly record struct EditOutcome(string? Refusal, ICommand? Inverse = null)
{
    /// <summary>The edit was spliced into the body.</summary>
    public static EditOutcome Applied { get; } = new(null);

    /// <summary>The edit was spliced into the body, and <paramref name="inverse"/> undoes it.</summary>
    public static EditOutcome UndoneBy(ICommand inverse) =>
        new(null, inverse ?? throw new ArgumentNullException(nameof(inverse)));

    /// <summary>The edit was refused, and this is why.</summary>
    public static EditOutcome Refused(string because) => new(because);

    /// <summary>
    /// The edit asked for what the body already says. <see cref="DocumentEditRun.Run"/> saves nothing
    /// and records nothing to undo: an undo step for an edit that did not happen is one that appears
    /// to do nothing when the user reaches it.
    /// </summary>
    public static EditOutcome Unchanged { get; } = new(null) { IsUnchanged = true };

    /// <summary>Whether the edit asked for what the body already says, so there is nothing to save.</summary>
    public bool IsUnchanged { get; private init; }

    /// <summary>Whether the body changed.</summary>
    public bool WasApplied => Refusal is null;
}
