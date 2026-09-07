namespace EtAlii.Adp.Context;

/// <summary>The action needs the user to confirm before it can be committed.</summary>
public sealed record ContextExecutionRequiresConfirmation(ContextConfirmationRequest Request) : ContextExecutionResult;
