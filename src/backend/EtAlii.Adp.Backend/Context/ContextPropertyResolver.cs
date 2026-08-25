namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// Routes purely by scope and by what providers report - it knows no property id of its own,
/// which is what lets a new provider be added by registration alone.
/// </summary>
public sealed class ContextPropertyResolver : IContextPropertyResolver
{
    private readonly IReadOnlyList<IContextPropertyProvider> _providers;

    public ContextPropertyResolver(IEnumerable<IContextPropertyProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
    }

    // <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var properties = new List<ContextPropertyDefinition>();
        foreach (var provider in ProvidersFor(target.Scope))
        {
            properties.AddRange(await provider.DescribeAsync(target, cancellationToken));
        }

        return properties;
    }

    // <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (var provider in ProvidersFor(target.Scope))
        {
            // Asked before told: the provider that describes a property is the one that owns
            // it, so a set never reaches a provider that would have to guess what the id means.
            var properties = await provider.DescribeAsync(target, cancellationToken);
            var match = properties.FirstOrDefault(property => property.Id == propertyId);
            if (match is null)
            {
                continue;
            }

            if (!match.IsEditable)
            {
                // Enforced here rather than trusted to the client. A grid that has not caught
                // up - or a caller that is not the grid at all - must not be able to write a
                // value the provider said was not writable.
                return ContextPropertyResult.Failure(match.ReadOnlyReason);
            }

            return await provider.SetAsync(target, propertyId, value, cancellationToken);
        }

        return ContextPropertyResult.Failure($"'{propertyId}' is not a property of what is selected.");
    }

    private IEnumerable<IContextPropertyProvider> ProvidersFor(ContextScope scope) =>
        _providers.Where(provider => provider.Scope == scope);
}
