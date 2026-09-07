namespace EtAlii.Adp.Context;

/// <summary>The action needs the user to pick from a tree of options before it can be committed.</summary>
public sealed record ContextExecutionRequiresChoice(ContextChoiceRequest Request) : ContextExecutionResult;
