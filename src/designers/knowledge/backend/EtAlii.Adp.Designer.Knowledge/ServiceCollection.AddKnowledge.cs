using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Designer.Knowledge;

public static class ServiceCollectionAddKnowledgeExtension
{
    /// <summary>
    /// Registers the Knowledge designer's services: how a session of a knowledge file is opened,
    /// and what a new one starts as. Called through the definition's <c>Build</c>, so the host
    /// names nothing of this module.
    /// </summary>
    public static void AddKnowledge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IKnowledgeDocumentStore, KnowledgeDocuments>();
        services.AddSingleton<ICommandHandler<KnowledgeEditCommand>, KnowledgeEditCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreKnowledgeFilesCommand>, RestoreKnowledgeFilesCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IKnowledgeDocumentStore>>, RestoreDocumentCommandHandler<IKnowledgeDocumentStore>>();
        services.AddSingleton<IDesignerSessionFactory, KnowledgeSessionFactory>();
        services.AddSingleton<IContextActionProvider, KnowledgeContextActionProvider>();
        services.AddSingleton<IDesignerDocumentTemplate, KnowledgeDocumentTemplate>();
        services.AddSingleton<IEntryRenameFollower, KnowledgeTargetRename>();
    }
}
