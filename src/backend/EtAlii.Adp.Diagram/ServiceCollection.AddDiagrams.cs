using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Registers the type-agnostic half of diagramming: the factories a module registers to
/// write an empty document body, the session factories that open one, and the registry that
/// correlates a connection's viewport with the stream it opened. The services a connection calls
/// are the host's, registered by <c>AddWorkspace</c> in <c>EtAlii.Adp.Workspace</c>.
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

        // One source of file events per project root, whoever listens there (code-structure FR-050).
        services.TryAddSingleton<ProjectFileEvents>();

        return services;
    }
}
