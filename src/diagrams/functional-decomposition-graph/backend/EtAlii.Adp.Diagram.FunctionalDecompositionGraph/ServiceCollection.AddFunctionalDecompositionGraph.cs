using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

public static class ServiceCollectionAddFunctionalDecompositionGraphExtension
{
    /// <summary>
    /// Registers what a functional decomposition graph needs to open and to follow its file: the
    /// store, the mapper, the session factory and the reload seam.
    /// </summary>
    /// <remarks>
    /// The providers and the validator's join to the Errors and Warnings panel arrive with task 13,
    /// so this list says how much of the module is wired.
    /// </remarks>
    public static IServiceCollection AddFunctionalDecompositionGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Without a document factory the host refuses to start: a type that declares an extension
        // must be creatable from the Add dialog.
        services.AddSingleton<IDiagramDocumentFactory, FdgDocumentFactory>();
        services.AddSingleton<FdgElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add so a
        // test that registers its own store keeps it.
        services.TryAddSingleton<IFdgDocumentStore, FdgDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, FdgSessionFactory>();

        // The reload seam: an external write to an .fdg body reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddSingleton<IDiagramDocumentReloader, FdgDocumentReloader>();

        // Selection: an element or connection of an .fdg resolves to a selection the panels read.
        services.AddSingleton<IContextSourceResolver, FdgContextSourceResolver>();

        // The commands, one handler each: the whole editable surface, and nothing writes the file
        // except through them. Every one's undo is the shared restore command, registered once.
        services.AddSingleton<ICommandHandler<AddFdgElementCommand>, AddFdgElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveFdgElementCommand>, RemoveFdgElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetFdgPlacementCommand>, SetFdgPlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetFdgSizeCommand>, SetFdgSizeCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectFdgElementsCommand>, ConnectFdgElementsCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectFdgConnectionCommand>, DisconnectFdgConnectionCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameFdgElementCommand>, RenameFdgElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetFdgDescriptionCommand>, SetFdgDescriptionCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameFdgConnectionCommand>, RenameFdgConnectionCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IFdgDocumentStore>>, RestoreDocumentCommandHandler<IFdgDocumentStore>>();

        return services;
    }
}
