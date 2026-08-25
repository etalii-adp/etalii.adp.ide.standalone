using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// Registers the context area: the per-connection selection and interaction stores, the
/// resolver that verifies a selection chain, and the resolver that aggregates every
/// registered <see cref="IContextActionProvider"/>.
/// </summary>
/// <remarks>
/// The providers and source resolvers themselves are not registered here - each belongs to
/// the area that contributes it (the hierarchy's, the history's, a diagram module's), which
/// is what keeps this method from having to name any of them.
/// </remarks>
public static class ServiceCollectionAddContextExtension
{
    public static IServiceCollection AddContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IContextInteractionStore, ContextInteractionStore>();
        services.AddSingleton<IContextActionResolver, ContextActionResolver>();
        services.AddSingleton<IContextSelectionStore, ContextSelectionStore>();
        services.AddSingleton<ContextSelectionResolver>();

        return services;
    }
}
