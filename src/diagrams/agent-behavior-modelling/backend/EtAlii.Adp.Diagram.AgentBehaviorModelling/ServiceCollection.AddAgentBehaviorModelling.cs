using EtAlii.Adp.Context;
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
    /// A drag is among them: <see cref="ArrangeAbmNodeCommand"/> writes the registration's
    /// <c>layout:</c> block, and the Markdown too when the drop changed the order.
    /// </remarks>
    public static void AddAgentBehaviorModelling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Without a document factory the host refuses to start: a type that declares an extension
        // must be creatable from the Add dialog. Written from the file's name, which titles the model.
        services.AddEmptyDocument(Diagram.AgentBehaviorModelling.Origin, baseName => AbmWriter.EmptyDocument(baseName, "\r\n"));
        services.AddSingleton<AbmElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd so a test that
        // registers its own store keeps it.
        services.TryAddSingleton<IAbmDocumentStore, AbmDocumentStore>();

        // The project's history, so a drag is one undo away like every other edit.
        services.AddDiagramSession(Diagram.AgentBehaviorModelling.Origin, (provider, opening) => new AbmSession(
            opening.BodyPath,
            opening.RegistrationPath,
            provider.GetRequiredService<IAbmDocumentStore>(),
            provider.GetRequiredService<AbmElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

        // The reload seam: an external write to a Markdown body reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddDiagramDocumentReloader<IAbmDocumentStore>(Diagram.AgentBehaviorModelling.Origin);

        // Selection, and what can be done to it: the palette, the menus and shortcuts, and the property grid.
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
        services.AddSingleton<ICommandHandler<ConnectAbmChildCommand>, ConnectAbmChildCommandHandler>();
        services.AddSingleton<ICommandHandler<ArrangeAbmNodeCommand>, ArrangeAbmNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreAbmArrangementCommand>, RestoreAbmArrangementCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IAbmDocumentStore>>, RestoreDocumentCommandHandler<IAbmDocumentStore>>();

        // The arrangement writes the registration, never the Markdown: it forgets the dragged positions.
        services.AddSingleton<ICommandHandler<ArrangeAbmCommand>, ArrangeAbmCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreAbmRegistrationCommand>, RestoreAbmRegistrationCommandHandler>();

        services.AddSingleton<IDiagramValidator, AbmValidator>();
    }
}
