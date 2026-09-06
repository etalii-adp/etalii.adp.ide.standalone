namespace EtAlii.Adp.Common;

/// <summary>The action needs the user to confirm before it can be committed.</summary>
public sealed record ContextExecutionRequiresConfirmation(ContextConfirmationRequest Request) : ContextExecutionResult;
