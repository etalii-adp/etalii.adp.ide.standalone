namespace EtAlii.Adp.Context;

/// <summary>Resolved action, paired with the provider that owns it.</summary>
public sealed record ContextActionOwner(IContextActionProvider Provider, ContextActionDefinition Action);

/// <summary>
/// Aggregates every <see cref="IContextActionProvider"/> registered for a scope, and
/// answers which provider owns a given action id or keyboard shortcut. Adding actions is
/// therefore a registration, never an edit here.
/// </summary>
public interface IContextActionResolver
{
    /// <summary>
    /// Every matching provider's contribution for <paramref name="target"/>, concatenated
    /// in registration order.
    /// </summary>
    ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken);

    /// <summary>The provider offering <paramref name="actionId"/> for this target, if any.</summary>
    ValueTask<ContextActionOwner?> ResolveByActionIdAsync(ContextTarget target, string actionId, CancellationToken cancellationToken);

    /// <summary>
    /// The provider offering an available action bound to <paramref name="shortcut"/> for
    /// this target, if any. An action reported unavailable resolves to nothing, so its
    /// shortcut is inert too.
    /// </summary>
    ValueTask<ContextActionOwner?> ResolveByShortcutAsync(ContextTarget target, ContextShortcutDefinition shortcut, CancellationToken cancellationToken);
}
