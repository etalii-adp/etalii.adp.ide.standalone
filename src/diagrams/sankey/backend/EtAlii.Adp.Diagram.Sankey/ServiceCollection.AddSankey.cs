using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Sankey;

public static class ServiceCollectionAddSankeyExtension
{
    /// <summary>
    /// Registers what a Sankey diagram needs to open, to follow its file and to be edited: the
    /// store, the mapper, the session factory, the reload seam, the selection resolver, the
    /// toolbox, action and property providers, one handler per command, and the validator.
    /// </summary>
    public static void AddSankey(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDiagramDocumentFactory, SankeyDocumentFactory>();
        services.AddSingleton<SankeyElementMapper>();

        // One document per path, shared by every connection viewing it; TryAdd so a test keeps its own.
        services.TryAddSingleton<ISankeyDocumentStore, SankeyDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, SankeySessionFactory>();
        services.AddSingleton<IDiagramDocumentReloader, SankeyDocumentReloader>();
        services.AddSingleton<IContextSourceResolver, SankeyContextSourceResolver>();

        services.AddSingleton<IDiagramToolboxProvider, SankeyToolboxProvider>();
        services.AddSingleton<IContextActionProvider, SankeyContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, SankeyContextPropertyProvider>();

        services.AddSingleton<ICommandHandler<AddSankeyNodeCommand>, AddSankeyNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectSankeyNodesCommand>, ConnectSankeyNodesCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveSankeyEntryCommand>, RemoveSankeyEntryCommandHandler>();
        services.AddSingleton<ICommandHandler<MoveSankeyNodeCommand>, MoveSankeyNodeCommandHandler>();
        services.AddSingleton<ICommandHandler<SetSankeyPropertyCommand>, SetSankeyPropertyCommandHandler>();
        services.AddSingleton<ICommandHandler<StepSankeyValueCommand>, StepSankeyValueCommandHandler>();
        services.AddSingleton<ICommandHandler<ScaleSankeyThicknessCommand>, ScaleSankeyThicknessCommandHandler>();
        services.AddSingleton<ICommandHandler<ArrangeSankeyCommand>, ArrangeSankeyCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<ISankeyDocumentStore>>, RestoreDocumentCommandHandler<ISankeyDocumentStore>>();

        services.AddSingleton<IDiagramValidator, SankeyValidator>();
    }
}
