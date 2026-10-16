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
    public static void AddDependencyGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The version marker and an empty element list, and nothing else: an empty graph is a valid
        // one, and a placeholder node would be the first thing every user deletes. The base name is
        // not written - a graph has no title line in its schema. CRLF, the house style, since a new
        // file has no style of its own to keep.
        services.AddEmptyDocument(Diagram.DependencyGraph.Origin, "dependencies: 1\r\nelements: []\r\n");
        services.AddSingleton<DependencyGraphElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add
        // so a test that registers its own store keeps it.
        services.TryAddSingleton<IDependencyGraphDocumentStore, DependencyGraphDocumentStore>();

        // The registration file is ignored: a `generic/dependencies` `.adp` carries nothing beyond
        // its MIME line, and a bare `.dgr` routes here with no registration at all. The project's
        // history, so a drag is one undo away like every other edit.
        services.AddDiagramSession(Diagram.DependencyGraph.Origin, (provider, opening) => new DependencyGraphSession(
            opening.BodyPath,
            provider.GetRequiredService<IDependencyGraphDocumentStore>(),
            provider.GetRequiredService<DependencyGraphElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

        // The reload seam: an external write to a graph reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddDiagramDocumentReloader<IDependencyGraphDocumentStore>(Diagram.DependencyGraph.Origin);

        // The commands, one handler each: the whole editable surface of a graph, and nothing
        // writes the file except through them. Nine where the timeline has ten - SetTimelineEnd
        // had no counterpart to fork, because nothing here has an end.
        services.AddSingleton<ICommandHandler<SetDependencyGraphPlacementCommand>, SetDependencyGraphPlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddDependencyGraphElementCommand>, AddDependencyGraphElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveDependencyGraphElementCommand>, RemoveDependencyGraphElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreLinesCommand<IDependencyGraphDocumentStore>>, RestoreLinesCommandHandler<IDependencyGraphDocumentStore>>();
        services.AddSingleton<ICommandHandler<RenameDependencyGraphElementCommand>, RenameDependencyGraphElementCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectDependencyGraphElementsCommand>, ConnectDependencyGraphElementsCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectDependencyGraphRelationCommand>, DisconnectDependencyGraphRelationCommandHandler>();
        services.AddSingleton<ICommandHandler<RelabelDependencyGraphRelationCommand>, RelabelDependencyGraphRelationCommandHandler>();
        services.AddSingleton<ICommandHandler<AddConnectedDependencyGraphElementCommand>, AddConnectedDependencyGraphElementCommandHandler>();

        // The context seams: selection, actions, properties and the palette.
        services.AddSingleton<IContextSourceResolver, DependencyGraphContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, DependencyGraphContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, DependencyGraphContextPropertyProvider>();
        services.AddSingleton<IDiagramToolboxProvider>(new DislToolboxProvider(Diagram.DependencyGraph.Origin, DependencyGraphDefinition.Definition));

        // The rules' join to the Errors and Warnings panel.
        services.AddSingleton<IDiagramValidator, DependencyGraphValidator>();
    }
}
