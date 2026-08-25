namespace EtAlii.Adp.Backend.Context;

/// <summary>The action needs a value from the user before it can be committed.</summary>
public sealed record ContextExecutionRequiresInput(ContextInputRequest Request) : ContextExecutionResult;
