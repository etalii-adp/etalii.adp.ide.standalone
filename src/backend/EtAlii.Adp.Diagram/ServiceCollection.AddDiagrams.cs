using EtAlii.Adp.Common;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Registers the type-agnostic half of diagramming: the factories a module registers to
/// write an empty document body, the session factories that open one, and the registry that
/// correlates a connection's viewport with the stream it opened.
/// </summary>
/// <remarks>
/// Nothing here names a diagram type. A module becomes creatable by registering an
/// <see cref="IDiagramDocumentFactory"/> and openable by registering an
/// <see cref="IDiagramSessionFactory"/>, both from its own extension method.
/// </remarks>
public static class ServiceCollectionAddDiagramsExtension
{
    public static IServiceCollection AddDiagrams(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DiagramDocumentFactories>();
        services.AddSingleton<DiagramSessionFactories>();
        services.AddSingleton<IDiagramViewportRegistry, DiagramViewportRegistry>();

        // The watcher-to-store bridge: an external write to an open diagram's body or .adp
        // reaches the owning store's Reload, which is what carries it to every open session
        // as pushed deltas (modular-text-editors Requirements 5.3, 5.5). Modules opt in by
        // registering an IDiagramDocumentReloader per origin.
        services.AddSingleton<DiagramDocumentReloadBridge>();

        // The editor family rides the same stream: what nothing diagram-shaped claims falls
        // through to the resolved editor (modular-text-editors Requirement 5.1's order). The
        // catalog tolerates a host that never ran AddEditorDefinitions - an older test host -
        // by answering empty rather than failing its container.
        services.AddSingleton<EditorSessionFactories>();
        services.TryAddSingleton<IEditorDefinitionCatalog>(svc =>
            new EditorDefinitionCatalog { All = svc.GetService<IReadOnlyList<EditorDefinition>>() ?? [] });
        services.AddSingleton<EditorResolver>(svc => new EditorResolver(svc.GetRequiredService<IEditorDefinitionCatalog>()));

        return services;
    }
}
