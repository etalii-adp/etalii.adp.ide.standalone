using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>Registers everything the agent activity diagram contributes to the host.</summary>
public static class ServiceCollectionAddAgentActivityDiagramExtension
{
    public static void AddAgentActivityDiagram(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Without a document factory the host refuses to start: a type that declares an extension
        // must be creatable from the Add dialog.
        services.AddSingleton<IDiagramDocumentFactory, AadDocumentFactory>();
        services.AddSingleton<AadElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add so a
        // test that registers its own store keeps it.
        services.TryAddSingleton<IAadDocumentStore, AadDocumentStore>();
        services.AddSingleton<IDiagramSessionFactory, AadSessionFactory>();

        // The reload seam: an agent's write to an .aad body reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddSingleton<IDiagramDocumentReloader, AadDocumentReloader>();
    }
}
