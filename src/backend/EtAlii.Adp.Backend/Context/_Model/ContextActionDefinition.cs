namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// One action a provider offers for a target. An action that currently cannot be
/// performed is reported with <see cref="Available"/> false and a reason rather than
/// omitted, so the consumer can show it disabled and explain why.
/// </summary>
public sealed record ContextActionDefinition(
    string Id,
    string Label,
    string Icon,
    ContextShortcutDefinition? Shortcut = null,
    bool Available = true,
    string UnavailableReason = "",
    IReadOnlyList<ContextActionGroupDefinition>? Children = null);
