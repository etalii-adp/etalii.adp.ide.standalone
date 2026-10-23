using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

public static class ServiceCollectionAddHierarchicalRequirementExtension
{
    /// <summary>
    /// Registers the Hierarchical Requirement Designer's services: how a session of a requirement document is opened.
    /// Called through the definition's <c>Build</c>, so the host names nothing of this module.
    /// </summary>
    public static void AddHierarchicalRequirement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFileWriter, FileWriter>();
        services.AddSingleton<IDesignerSessionFactory, HierarchicalRequirementSessionFactory>();
    }
}
