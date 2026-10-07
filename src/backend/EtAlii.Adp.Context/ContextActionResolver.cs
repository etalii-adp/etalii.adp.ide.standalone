using EtAlii.Adp.Documents.Wire;
namespace EtAlii.Adp.Context;

/// <summary>
/// Routes purely by scope and by what providers report - it knows no action id and no
/// shortcut of its own, which is what lets a new provider be added by registration alone.
/// </summary>
public sealed class ContextActionResolver(IEnumerable<IContextActionProvider> providers) : IContextActionResolver
{
    private readonly IReadOnlyList<IContextActionProvider> _providers = providers.ToList();

    // <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        var groups = new List<ContextActionGroupDefinition>();
        foreach (var provider in ProvidersFor(target.Scope))
        {
            groups.AddRange(await provider.DiscoverAsync(target, cancellationToken));
        }

        return groups;
    }

    // <inheritdoc />
    public ValueTask<ContextActionOwner?> ResolveByActionIdAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
        ResolveAsync(target, action => action.Id == actionId, cancellationToken);

    // <inheritdoc />
    public ValueTask<ContextActionOwner?> ResolveByShortcutAsync(ContextTarget target, ContextShortcutDefinition shortcut, CancellationToken cancellationToken) =>
        ResolveAsync(target, action => action is { Available: true, Shortcut: { } bound } && bound.Matches(shortcut), cancellationToken);

    private async ValueTask<ContextActionOwner?> ResolveAsync(
        ContextTarget target, Func<ContextActionDefinition, bool> predicate, CancellationToken cancellationToken)
    {
        foreach (var provider in ProvidersFor(target.Scope))
        {
            var groups = await provider.DiscoverAsync(target, cancellationToken);
            var match = Flatten(groups).FirstOrDefault(predicate);
            if (match is not null)
            {
                return new ContextActionOwner(provider, match);
            }
        }

        return null;
    }

    private IEnumerable<IContextActionProvider> ProvidersFor(ContextScope scope) =>
        _providers.Where(provider => provider.Scope == scope);

    /// <summary>Walks submenu children too, so a nested action is as resolvable as a top-level one.</summary>
    private static IEnumerable<ContextActionDefinition> Flatten(IEnumerable<ContextActionGroupDefinition> groups)
    {
        foreach (var action in groups.SelectMany(group => group.Actions))
        {
            yield return action;

            if (action.Children is not { } children)
            {
                continue;
            }

            foreach (var nested in Flatten(children))
            {
                yield return nested;
            }
        }
    }
}
