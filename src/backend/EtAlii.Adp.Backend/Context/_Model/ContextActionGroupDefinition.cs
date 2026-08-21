namespace EtAlii.Adp.Backend.Context;

/// <summary>One provider's contribution: a set of actions the consumer renders together.</summary>
public sealed record ContextActionGroupDefinition(IReadOnlyList<ContextActionDefinition> Actions);
