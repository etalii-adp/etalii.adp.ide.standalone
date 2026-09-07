namespace EtAlii.Adp.Context;

/// <summary>
/// One node of a choice dialog's option tree. A node that is not <paramref name="Selectable"/>
/// is a group: shown, labelled and expandable, never the answer. A selectable node's
/// <paramref name="Id"/> is exactly what comes back as the submitted value.
/// </summary>
/// <param name="NameSuppressedReason">
/// Non-empty: this option wants no name, and the dialog renders this sentence where the name
/// field would be. The condition and its explanation are one value on purpose - a boolean
/// could be set without a sentence, taking the field away and leaving nothing in its place.
/// </param>
/// <param name="UnavailableReason">
/// Non-empty: the option is shown but cannot be chosen, and this is the reason the user reads.
/// The general offered-but-not-choosable idiom rather than a folder-specific one, matching
/// <c>ContextAction.UnavailableReason</c>; it applies wherever hiding an option would leave
/// the user wondering where it went.
/// </param>
public sealed record ContextOptionNode(
    string Id,
    string Label,
    bool Selectable,
    IReadOnlyList<ContextOptionNode>? Children = null,
    string SuggestedValue = "",
    string Description = "",
    string Icon = "",
    string NameSuppressedReason = "",
    string UnavailableReason = "");
