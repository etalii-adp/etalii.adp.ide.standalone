using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// Registers what every kind of tool opens its sessions through: the session factories the modules
/// registered, by kind, and the registry that correlates a connection's viewport with the diagram
/// session it opened (code-structure FR-065).
/// </summary>
public static class ServiceCollectionAddToolSessionsExtension
{
    public static void AddToolSessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DiagramSessionFactories>();
        services.AddSingleton<DesignerSessionFactories>();
        services.AddSingleton<EditorSessionFactories>();
        services.AddSingleton<IDiagramViewportRegistry, DiagramViewportRegistry>();
    }
}
