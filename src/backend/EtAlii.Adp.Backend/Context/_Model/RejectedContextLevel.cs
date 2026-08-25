namespace EtAlii.Adp.Backend.Context;

/// <summary>One level of a selection that did not resolve, with the reason it was rejected.</summary>
public sealed record RejectedContextLevel(string Reason) : ContextLevelResolution;
