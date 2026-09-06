using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using Serilog;

namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// Routes purely by scope and by what providers report - it knows no property id of its own,
/// which is what lets a new provider be added by registration alone.
/// </summary>
public sealed class ContextPropertyResolver : IContextPropertyResolver
{
    private static readonly ILogger _logger = Log.ForContext<ContextPropertyResolver>();

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
            // One broken provider costs its own rows and nothing more: the panel shows what
            // the healthy providers said rather than going blank, the same rule
            // ContextServiceImpl.WithActionsAsync already applies to actions.
            properties.AddRange(await SafelyDescribeAsync(provider, target, cancellationToken));
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
            // A provider that cannot say what it owns is skipped rather than fatal - the
            // property being written may well belong to one that still answers.
            var properties = await SafelyDescribeAsync(provider, target, cancellationToken);
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

            // Deliberately unguarded: a write that failed must never be reported as accepted,
            // so the owning provider's own failure travels up rather than being swallowed here.
            return await provider.SetAsync(target, propertyId, value, cancellationToken);
        }

        return ContextPropertyResult.Failure($"'{propertyId}' is not a property of what is selected.");
    }

    /// <summary>
    /// What <paramref name="provider"/> describes, or nothing at all when it fails - a
    /// module's fault costs its own rows, never the whole answer. Cancellation is not a
    /// provider's fault and travels up.
    /// </summary>
    private static async ValueTask<IReadOnlyList<ContextPropertyDefinition>> SafelyDescribeAsync(
        IContextPropertyProvider provider,
        ContextTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.DescribeAsync(target, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning(
                exception,
                "{Provider} could not describe the properties of {TargetPath}; its rows are missing from this answer",
                provider.GetType().Name,
                target.ResolvedFullPath);
            return [];
        }
    }

    private IEnumerable<IContextPropertyProvider> ProvidersFor(ContextScope scope) =>
        _providers.Where(provider => provider.Scope == scope);
}
