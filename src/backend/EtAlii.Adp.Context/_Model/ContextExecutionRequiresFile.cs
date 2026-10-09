namespace EtAlii.Adp.Context;

/// <summary>The action needs the user to pick a file of the workspace before it can run.</summary>
public sealed record ContextExecutionRequiresFile(ContextFileRequest Request) : ContextExecutionResult;
