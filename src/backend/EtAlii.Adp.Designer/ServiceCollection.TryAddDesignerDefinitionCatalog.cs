using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Designer;

public static class ServiceCollectionTryAddDesignerDefinitionCatalogExtension
{
    /// <summary>
    /// Registers the designer catalog unless one is registered already. It reads the discovered
    /// definitions when it is first asked, not when it is registered, so it holds the same
    /// designers whichever of its callers ran first - the area that needs the catalog to route
    /// or the host registering what discovery found - and answers empty in a host that
    /// registered none.
    /// </summary>
    public static void TryAddDesignerDefinitionCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDesignerDefinitionCatalog>(provider =>
            new DesignerDefinitionCatalog { All = provider.GetService<IReadOnlyList<DesignerDefinition>>() ?? [] });
    }
}
