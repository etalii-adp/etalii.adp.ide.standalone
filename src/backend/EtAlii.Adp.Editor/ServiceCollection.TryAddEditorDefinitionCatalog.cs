using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Editor;

public static class ServiceCollectionTryAddEditorDefinitionCatalogExtension
{
    /// <summary>
    /// Registers the editor catalog unless one is registered already - the editor family's mirror of
    /// <c>TryAddDesignerDefinitionCatalog</c>. It reads the discovered definitions when it is first
    /// asked, not when it is registered, so it holds the same editors whichever of its callers ran
    /// first, and answers empty in a host that never ran <c>AddEditorDefinitions</c>, such as an
    /// older test host.
    /// </summary>
    public static void TryAddEditorDefinitionCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEditorDefinitionCatalog>(provider =>
            new EditorDefinitionCatalog { All = provider.GetService<IReadOnlyList<EditorDefinition>>() ?? [] });
    }
}
