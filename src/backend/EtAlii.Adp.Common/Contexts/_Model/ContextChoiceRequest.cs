namespace EtAlii.Adp.Common;

/// <summary>A dialog asking the user to pick one option out of a grouped tree, described entirely as data.</summary>
/// <param name="EmptyMessage">Shown in place of the tree when <paramref name="Options"/> is empty.</param>
public sealed record ContextChoiceRequest(
    string Title,
    string Icon,
    string ConfirmLabel,
    IReadOnlyList<ContextOptionNode> Options,
    string EmptyMessage,
    ContextTextFieldRequest? NameField = null);
