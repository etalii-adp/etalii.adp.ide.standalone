using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;

using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Registers the hierarchy area: the per-connection model of a project's folders and files,
/// the router that decides which diagram type a file on disk belongs to, and the context
/// seams the hierarchy contributes - what can be done to an entry, how an entry id resolves,
/// and the Add action that creates a new diagram.
/// </summary>
public static class ServiceCollectionAddHierarchyExtension
{
    public static IServiceCollection AddHierarchy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHierarchyModelStore, HierarchyModelStore>();

        // Which type a file on disk belongs to - by its .adp first line, or by a declared
        // extension for a body dropped in without one. The catalog it reads is registered
        // by AddCommands.
        services.AddSingleton<DiagramFileRouter>();

        // Registered through IContextActionProvider so the resolver picks them up from
        // IEnumerable<IContextActionProvider>; likewise IContextSourceResolver. A later
        // module contributing its own is one line in its own extension and no change here.
        services.AddSingleton<IContextActionProvider, HierarchyContextActionProvider>();
        services.AddSingleton<IContextSourceResolver, HierarchyContextSourceResolver>();

        // Built by hand: the provider's other constructor takes the diagram definitions a
        // test hands it, and letting the container choose between the two would leave which
        // one it picks to depend on what else happens to be registered.
        services.AddSingleton<IContextActionProvider>(provider =>
            new AddDiagramContextActionProvider(
                provider.GetRequiredService<IHistoryStackStore>(),
                provider.GetRequiredService<DiagramDocumentFactories>(),
                provider.GetRequiredService<IDiagramDefinitionCatalog>(),
                provider.GetRequiredService<DiagramFileRouter>())
        );

        // The editor family's contributions to the hierarchy scope: "Open as text"/"Open
        // with…" on diagram-claimed files, and the read-only file facts of any text file.
        // Registered once for the whole family, never per editor module (modular-text-editors
        // Requirements 5.2, 4.4, 9.1).
        services.AddSingleton<IContextActionProvider, OpenAsTextContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, EditorFilePropertyProvider>();

        return services;
    }
}
