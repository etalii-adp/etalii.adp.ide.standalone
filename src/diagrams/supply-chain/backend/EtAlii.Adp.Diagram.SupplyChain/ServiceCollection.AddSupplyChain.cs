using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.SupplyChain;

public static class ServiceCollectionAddSupplyChainExtension
{
    /// <summary>
    /// Registers what a supply chain diagram needs to open, to follow its file, to be edited and to
    /// trace its selection: the store, the mapper, the selection registry, the session factory, the
    /// reload seam, the selection resolver, the toolbox, action and property providers, one handler
    /// per command, and the validator.
    /// </summary>
    public static void AddSupplyChain(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDiagramDocumentFactory, SupplyChainDocumentFactory>();
        services.AddSingleton<SupplyChainElementMapper>();

        // One document per path, shared by every connection viewing it; TryAdd so a test keeps its own.
        services.TryAddSingleton<ISupplyChainDocumentStore, SupplyChainDocumentStore>();

        // What each connection has selected in each diagram: the resolver writes it, the sessions
        // read it and re-render with the chain through it marked.
        services.AddSingleton<SupplyChainSelections>();

        services.AddSingleton<IDiagramSessionFactory, SupplyChainSessionFactory>();
        services.AddSingleton<IDiagramDocumentReloader, SupplyChainDocumentReloader>();
        services.AddSingleton<IContextSourceResolver, SupplyChainContextSourceResolver>();

        services.AddSingleton<IDiagramToolboxProvider, SupplyChainToolboxProvider>();
        services.AddSingleton<IContextActionProvider, SupplyChainContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, SupplyChainContextPropertyProvider>();

        services.AddSingleton<ICommandHandler<AddSupplyChainNodeCommand>, AddSupplyChainNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<AddSupplyChainGroupCommand>, AddSupplyChainGroupCommandHandler>();
        services.AddSingleton<ICommandHandler<MoveSupplyChainEntryCommand>, MoveSupplyChainEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectSupplyChainNodesCommand>, ConnectSupplyChainNodesCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveSupplyChainEntryCommand>, RemoveSupplyChainEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<PlaceSupplyChainNodesCommand>, PlaceSupplyChainNodesCommandHandler>();
        services.AddSingleton<ICommandHandler<ArrangeSupplyChainCommand>, ArrangeSupplyChainCommandHandler>();
        services.AddSingleton<ICommandHandler<SetSupplyChainPropertyCommand>, SetSupplyChainPropertyCommandHandler>();
        services.AddSingleton<ICommandHandler<StepSupplyChainValueCommand>, StepSupplyChainValueCommandHandler>();
        services.AddSingleton<ICommandHandler<GroupSupplyChainNodeCommand>, GroupSupplyChainNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<ISupplyChainDocumentStore>>, RestoreDocumentCommandHandler<ISupplyChainDocumentStore>>();

        services.AddSingleton<IDiagramValidator, SupplyChainValidator>();
    }
}
