namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The per-option values a caller decides while the subject is still known, computed once when
/// the prompt is built so that picking an option costs no round trip.
/// </summary>
/// <remarks>
/// These three travel together because they are decided together: whether a type wants a name,
/// what that name should default to, and whether the type can be chosen here at all are one
/// judgement about one option, made with the folder and the definition both in hand. Deciding
/// them in three callbacks would mean reading the same subject three times and leaving room for
/// two of the answers to disagree.
/// <para>
/// A group receives <see cref="None"/>: it is a heading rather than something that can be
/// created, so none of the three means anything for it.
/// </para>
/// </remarks>
/// <param name="SuggestedValue">
/// What a text field beside the tree should take when this option is picked, as long as the
/// user has not typed a value of their own. Empty when the option suggests nothing - including
/// when it wants no name at all.
/// </param>
/// <param name="NameSuppressedReason">
/// Non-empty: this option wants no name, and the dialog renders this sentence where the name
/// field would be.
/// </param>
/// <param name="UnavailableReason">
/// Non-empty: the option is shown but cannot be chosen, and this is the reason the user reads.
/// </param>
public sealed record ContextOptionAnnotations(
    string SuggestedValue = "",
    string NameSuppressedReason = "",
    string UnavailableReason = "")
{
    /// <summary>Nothing to say about this option - what a group gets, and the default.</summary>
    public static ContextOptionAnnotations None { get; } = new();
}
