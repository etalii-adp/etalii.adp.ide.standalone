using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

public static class ServiceCollectionAddFunctionalDecompositionGraphExtension
{
    /// <summary>
    /// Registers what a functional decomposition graph needs to open and to follow its file: the
    /// store, the mapper, the session factory and the reload seam.
    /// </summary>
    /// <remarks>
    /// The commands, the providers and the validator's join to the Errors and Warnings panel arrive
    /// with the tasks that write them (12 and 13), so this list says how much of the module is wired.
    /// </remarks>
    public static IServiceCollection AddFunctionalDecompositionGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<FdgElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add so a
        // test that registers its own store keeps it.
        services.TryAddSingleton<IFdgDocumentStore, FdgDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, FdgSessionFactory>();

        // The reload seam: an external write to an .fdg body reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddSingleton<IDiagramDocumentReloader, FdgDocumentReloader>();

        return services;
    }
}
