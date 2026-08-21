namespace EtAlii.Adp.Backend.Context;

/// <summary>A dialog asking the user for a single value, described entirely as data.</summary>
public sealed record ContextInputRequest(string Title, string Icon, string FieldLabel, string InitialValue, string ConfirmLabel);

/// <summary>A dialog asking the user to confirm or cancel, described entirely as data.</summary>
public sealed record ContextConfirmationRequest(string Title, string Icon, string Message, string ConfirmLabel, bool Danger);
