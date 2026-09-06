namespace EtAlii.Adp.Common;

/// <summary>The action could not start, with a reason meant for the user.</summary>
public sealed record ContextExecutionFailed(string Message) : ContextExecutionResult;
