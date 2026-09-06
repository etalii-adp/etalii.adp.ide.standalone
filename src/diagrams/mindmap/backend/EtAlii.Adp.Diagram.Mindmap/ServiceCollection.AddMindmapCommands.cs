using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Registers the mindmap module's command handlers and the document store they act on. The
/// module owns this rather than adding its handlers to core's <c>AddCommands</c>, which would
/// have core reference this assembly - the dependency direction Requirement 13.4 forbids. The
/// host calls it beside <c>AddCommands</c>; a test that needs the real handlers calls it too.
/// </summary>
public static class ServiceCollectionAddMindmapCommandsExtension
{
    public static IServiceCollection AddMindmapCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMindmapDocumentStore, MindmapDocumentStore>();

        // The type's rules, resolved by origin through core's DiagramValidators registry
        // (errors-and-warnings-panel Requirement 3.1).
        services.AddSingleton<IDiagramValidator, MindmapValidator>();

        services.AddSingleton<ICommandHandler<AddChildNodeCommand>, AddChildNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<AddSiblingNodeCommand>, AddSiblingNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<MoveNodeCommand>, MoveNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveNodeCommand>, RemoveNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreSubtreeCommand>, RestoreSubtreeCommandHandler>();
        services.AddSingleton<ICommandHandler<SetNodeTextCommand>, SetNodeTextCommandHandler>();
        services.AddSingleton<ICommandHandler<SetNodeNotesCommand>, SetNodeNotesCommandHandler>();
        services.AddSingleton<ICommandHandler<SetNodeLinkCommand>, SetNodeLinkCommandHandler>();

        return services;
    }
}
