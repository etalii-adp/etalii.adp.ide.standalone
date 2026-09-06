namespace EtAlii.Adp.Common;

/// <summary>
/// A text field a dialog shows beside its primary answer. The value the user types comes
/// back as the submission's text, judged by the same validation the field showed while it
/// was being typed.
/// </summary>
public sealed record ContextTextFieldRequest(string Label, string InitialValue = "");
