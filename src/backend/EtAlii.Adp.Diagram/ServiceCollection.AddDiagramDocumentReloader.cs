using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram;

public static class ServiceCollectionAddDiagramDocumentReloaderExtension
{
    /// <summary>
    /// Registers the reload seam of the diagram type at <paramref name="origin"/>, over the store a
    /// module registered as <typeparamref name="TStore"/>.
    /// </summary>
    public static void AddDiagramDocumentReloader<TStore>(this IServiceCollection services, ToolOrigin origin)
        where TStore : IDiagramDocumentStore
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(origin);

        services.AddSingleton<IDiagramDocumentReloader>(provider =>
            new DiagramDocumentReloader<TStore>(origin, provider.GetRequiredService<TStore>()));
    }
}
