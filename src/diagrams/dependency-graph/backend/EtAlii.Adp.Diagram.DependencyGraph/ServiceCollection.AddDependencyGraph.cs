using EtAlii.Adp.Documents;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Registers everything the dependency graph module contributes, in one place.
/// </summary>
/// <remarks>
/// Invoked through <see cref="Diagram.DependencyGraph"/>'s <c>Build</c> hook when discovery finds
/// this module - the host names nothing here, and core compiles and tests with this assembly
/// absent.
/// </remarks>
public static class ServiceCollectionAddDependencyGraphExtension
{
    /// <summary>Adds the dependency graph diagram module's seams.</summary>
    public static IServiceCollection AddDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDiagramDocumentFactory, DependencyGraphDocumentFactory>();
        services.AddSingleton<DependencyGraphElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add
        // so a test that registers its own store keeps it.
        services.TryAddSingleton<IDependencyGraphDocumentStore, DependencyGraphDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, DependencyGraphSessionFactory>();

        // The reload seam: an external write to a graph reaches the store, and through it every
        // open session.
        services.AddSingleton<IDiagramDocumentReloader, DependencyGraphDocumentReloader>();

        // The commands, one handler each: the whole editable surface of a graph, and nothing
        // writes the file except through them. Nine where the timeline has ten - SetTimelineEnd
        // had no counterpart to fork, because nothing here has an end.
        services.AddSingleton<ICommandHandler<SetDependencyGraphPlacementCommand>, SetDependencyGraphPlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddDependencyGraphElementCommand>, AddDependencyGraphElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveDependencyGraphElementCommand>, RemoveDependencyGraphElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDependencyGraphLinesCommand>, RestoreDependencyGraphLinesCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameDependencyGraphElementCommand>, RenameDependencyGraphElementCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectDependencyGraphElementsCommand>, ConnectDependencyGraphElementsCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectDependencyGraphRelationCommand>, DisconnectDependencyGraphRelationCommandHandler>();
        services.AddSingleton<ICommandHandler<RelabelDependencyGraphRelationCommand>, RelabelDependencyGraphRelationCommandHandler>();
        services.AddSingleton<ICommandHandler<AddConnectedDependencyGraphElementCommand>, AddConnectedDependencyGraphElementCommandHandler>();

        // The context seams: selection, actions, properties and the palette.
        services.AddSingleton<IContextSourceResolver, DependencyGraphContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, DependencyGraphContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, DependencyGraphContextPropertyProvider>();
        services.AddSingleton<IDiagramToolboxProvider, DependencyGraphToolboxProvider>();

        // The rules' join to the Errors and Warnings panel.
        services.AddSingleton<IDiagramValidator, DependencyGraphValidator>();

        return services;
    }
}
