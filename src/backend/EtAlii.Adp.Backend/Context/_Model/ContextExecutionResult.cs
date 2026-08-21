namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// What a provider wants to happen next after an action is triggered: gather a value,
/// have the user confirm, or nothing at all (the action already did its work).
/// </summary>
public abstract record ContextExecutionResult
{
    public sealed record RequiresInput(ContextInputRequest Request) : ContextExecutionResult;

    public sealed record RequiresConfirmation(ContextConfirmationRequest Request) : ContextExecutionResult;

    public sealed record Completed : ContextExecutionResult;

    public sealed record Failed(string Message) : ContextExecutionResult;
}
