using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
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

        // Selection: an element or a relation of an activity file resolves to a selection the panels read.
        services.AddSingleton<IContextSourceResolver, AadContextSourceResolver>();

        // Every finding, to the Errors and Warnings panel: what breaks a rule is said, never removed.
        services.AddSingleton<IDiagramValidator, AadValidator>();

        // What can be done to it: the palette, the menus and gestures, and the property grid.
        services.AddSingleton<IDiagramToolboxProvider, AadToolboxProvider>();
        services.AddSingleton<IContextActionProvider, AadContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, AadContextPropertyProvider>();

        // The commands, one handler each: nothing writes the file except through them. Every one's
        // undo is the shared restore command, which refuses once an agent has written the file since.
        services.AddSingleton<ICommandHandler<AddAadElementCommand>, AddAadElementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddAadRowCommand>, AddAadRowCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveAadElementCommand>, RemoveAadElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetAadAttributeCommand>, SetAadAttributeCommandHandler>();
        services.AddSingleton<ICommandHandler<SetAadTaskStatusCommand>, SetAadTaskStatusCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectAadCommand>, ConnectAadCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectAadCommand>, DisconnectAadCommandHandler>();
        services.AddSingleton<ICommandHandler<PinAadElementCommand>, PinAadElementCommandHandler>();
        services.AddSingleton<ICommandHandler<UnpinAadElementCommand>, UnpinAadElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetAadGroupCommand>, SetAadGroupCommandHandler>();
        services.AddSingleton<ICommandHandler<SetAadShowArchivedCommand>, SetAadShowArchivedCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IAadDocumentStore>>, RestoreDocumentCommandHandler<IAadDocumentStore>>();
    }
}
