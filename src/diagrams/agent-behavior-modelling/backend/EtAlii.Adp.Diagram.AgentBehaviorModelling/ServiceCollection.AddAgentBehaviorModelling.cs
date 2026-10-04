using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

public static class ServiceCollectionAddAgentBehaviorModellingExtension
{
    /// <summary>
    /// Registers what a behavior model needs to open, to follow its Markdown and to be edited: the
    /// store, the mapper, the session factory, the reload seam, the selection resolver, the
    /// toolbox, action and property providers, one handler per command, and the validator.
    /// </summary>
    /// <remarks>
    /// A drag is not among the commands: it is core's <c>SetRegistrationLayoutCommand</c>, which
    /// writes the registration's <c>layout:</c> block and is registered by the hierarchy.
    /// </remarks>
    public static IServiceCollection AddAgentBehaviorModelling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Without a document factory the host refuses to start: a type that declares an extension
        // must be creatable from the Add dialog.
        services.AddSingleton<IDiagramDocumentFactory, AbmDocumentFactory>();
        services.AddSingleton<AbmElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd so a test that
        // registers its own store keeps it.
        services.TryAddSingleton<IAbmDocumentStore, AbmDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, AbmSessionFactory>();
        services.AddSingleton<IDiagramDocumentReloader, AbmDocumentReloader>();
        services.AddSingleton<IContextSourceResolver, AbmContextSourceResolver>();
        services.AddSingleton<IDiagramToolboxProvider, AbmToolboxProvider>();
        services.AddSingleton<IContextActionProvider, AbmContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, AbmContextPropertyProvider>();

        // The commands, one handler each: nothing writes the Markdown except through them, and every
        // one's undo is the shared restore command.
        services.AddSingleton<ICommandHandler<AddAbmNodeCommand>, AddAbmNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveAbmNodeCommand>, RemoveAbmNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameAbmNodeCommand>, RenameAbmNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<SetAbmNodeKindCommand>, SetAbmNodeKindCommandHandler>();
        services.AddSingleton<ICommandHandler<SetAbmNotesCommand>, SetAbmNotesCommandHandler>();
        services.AddSingleton<ICommandHandler<MoveAbmNodeCommand>, MoveAbmNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IAbmDocumentStore>>, RestoreDocumentCommandHandler<IAbmDocumentStore>>();

        // The arrangement writes the registration, never the Markdown: it forgets the dragged positions.
        services.AddSingleton<ICommandHandler<ArrangeAbmCommand>, ArrangeAbmCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreAbmRegistrationCommand>, RestoreAbmRegistrationCommandHandler>();

        services.AddSingleton<IDiagramValidator, AbmValidator>();

        return services;
    }
}
