using EtAlii.Adp.Common;
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
        services.AddSingleton<IContextPropertyResolver, ContextPropertyResolver>();
        services.AddSingleton<IContextSelectionStore, ContextSelectionStore>();

        // The seam the history stack uses to tell a project that a command succeeded with
        // something worth saying. Registered here because this is where the stream it writes to
        // lives; History depends on the interface only.
        services.AddSingleton<IContextNoticeSink, ContextNoticeSink>();
        services.AddSingleton<ContextSelectionResolver>();

        return services;
    }
}
