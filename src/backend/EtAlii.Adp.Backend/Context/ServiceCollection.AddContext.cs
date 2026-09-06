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
        // History availability onto the context stream: registered here rather than in
        // AddHistoryActions because the broadcaster is Context's own machinery - it aggregates
        // through IContextActionResolver and IContextSelectionStore, registered just above, and
        // it extracts with Context (backend-project-decomposition task 10). Program.cs resolves
        // it eagerly at startup; that line is a resolution and stays where it is.
        services.AddSingleton<HistoryActionsBroadcaster>();

        // The seam the history stack uses to tell a project that a command succeeded with
        // something worth saying. Registered here because this is where the stream it writes to
        // lives; History depends on the interface only.
        services.AddSingleton<IContextNoticeSink, ContextNoticeSink>();
        services.AddSingleton<ContextSelectionResolver>();

        return services;
    }
}
