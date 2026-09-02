namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// One node of a choice dialog's option tree. A node that is not <paramref name="Selectable"/>
/// is a group: shown, labelled and expandable, never the answer. A selectable node's
/// <paramref name="Id"/> is exactly what comes back as the submitted value.
/// </summary>
public sealed record ContextOptionNode(
    string Id,
    string Label,
    bool Selectable,
    IReadOnlyList<ContextOptionNode>? Children = null,
    string SuggestedValue = "",
    string Description = "",
    string Icon = "");
