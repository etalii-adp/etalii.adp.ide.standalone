using EtAlii.Adp.Context;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

public static class ServiceCollectionAddHierarchicalRequirementExtension
{
    /// <summary>
    /// Registers the Hierarchical Requirement Designer's services: how a session of a requirement document is opened, the
    /// commands an edit of one runs through the history, the keys that add an entry, and what a new one starts as.
    /// Called through the definition's <c>Build</c>, so the host names nothing of this module.
    /// </summary>
    public static void AddHierarchicalRequirement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFileWriter, FileWriter>();
        services.AddSingleton<IHierarchicalRequirementDocumentStore, HierarchicalRequirementDocuments>();
        services.AddSingleton<ICommandHandler<HierarchicalRequirementEditCommand>, HierarchicalRequirementEditCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreRequirementFilesCommand>, RestoreRequirementFilesCommandHandler>();
        services.AddSingleton<IDesignerSessionFactory, HierarchicalRequirementSessionFactory>();
        services.AddSingleton<IDesignerDocumentTemplate, HierarchicalRequirementDocumentTemplate>();
        services.AddSingleton<IContextActionProvider, HierarchicalRequirementContextActionProvider>();
    }
}
