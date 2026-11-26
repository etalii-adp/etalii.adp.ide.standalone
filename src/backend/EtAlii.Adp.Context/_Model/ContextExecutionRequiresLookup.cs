namespace EtAlii.Adp.Context;

/// <summary>The action needs the user to pick an element of the workspace in the lookup before it can run.</summary>
public sealed record ContextExecutionRequiresLookup(ContextLookupRequest Request) : ContextExecutionResult;
