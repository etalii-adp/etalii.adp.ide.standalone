using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Registers the type-agnostic half of diagramming: the factories a module registers to
/// write an empty document body. The session factories that open one and the viewport registry are
/// the sessions project's, registered by <c>AddToolSessions</c> in <c>EtAlii.Adp.Sessions</c>; the
/// services a connection calls are the host's, registered by <c>AddWorkspace</c> in
/// <c>EtAlii.Adp.Workspace</c>.
/// </summary>
/// <remarks>
/// Nothing here names a diagram type. A module becomes creatable by registering an
/// <see cref="IDiagramDocumentFactory"/> and openable by registering an
/// <c>IDiagramSessionFactory</c>, both from its own extension method.
/// </remarks>
public static class ServiceCollectionAddDiagramsExtension
{
    public static IServiceCollection AddDiagrams(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DiagramDocumentFactories>();

        // One source of file events per project root, whoever listens there (code-structure FR-050).
        services.TryAddSingleton<ProjectFileEvents>();

        return services;
    }
}
