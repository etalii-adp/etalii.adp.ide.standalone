using EtAlii.Adp.Diagram;

using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Backend.Diagrams;

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

        return services;
    }
}
