using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Registers everything the timeline module contributes, in one place.
/// </summary>
/// <remarks>
/// Invoked through <see cref="Diagram.Timeline"/>'s <c>Build</c> hook when discovery finds this
/// module - the host names nothing here, and core compiles and tests with this assembly absent.
/// Grows one line per seam as the module's tasks land, so at any commit it registers exactly
/// what exists.
/// </remarks>
public static class ServiceCollectionAddTimelineExtension
{
    /// <summary>Adds the timeline diagram module's seams.</summary>
    public static IServiceCollection AddTimeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDiagramDocumentFactory, TimelineDocumentFactory>();
        services.AddSingleton<TimelineElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add
        // so a test that registers its own store keeps it.
        services.TryAddSingleton<ITimelineDocumentStore, TimelineDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, TimelineSessionFactory>();

        // The reload seam: an external write to a timeline reaches the store, and through it
        // every open session (modular-text-editors Requirement 5.3).
        services.AddSingleton<IDiagramDocumentReloader, TimelineDocumentReloader>();

        // The commands, one handler each: the whole editable surface of a timeline
        // (Requirement 11.1), and nothing writes the file except through them.
        services.AddSingleton<ICommandHandler<SetTimelinePlacementCommand>, SetTimelinePlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddTimelineElementCommand>, AddTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveTimelineElementCommand>, RemoveTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreTimelineLinesCommand>, RestoreTimelineLinesCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameTimelineElementCommand>, RenameTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectTimelineElementsCommand>, ConnectTimelineElementsCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectTimelineConnectionCommand>, DisconnectTimelineConnectionCommandHandler>();
        services.AddSingleton<ICommandHandler<RelabelTimelineConnectionCommand>, RelabelTimelineConnectionCommandHandler>();
        services.AddSingleton<ICommandHandler<SetTimelineEndCommand>, SetTimelineEndCommandHandler>();
        services.AddSingleton<ICommandHandler<AddConnectedTimelineElementCommand>, AddConnectedTimelineElementCommandHandler>();


        // The context seams: selection, actions, properties and the palette.
        services.AddSingleton<IContextSourceResolver, TimelineContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, TimelineContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, TimelineContextPropertyProvider>();
        services.AddSingleton<IDiagramToolboxProvider, TimelineToolboxProvider>();

        // The rules' join to the Errors and Warnings panel.
        services.AddSingleton<IDiagramValidator, TimelineValidator>();

        return services;
    }
}
