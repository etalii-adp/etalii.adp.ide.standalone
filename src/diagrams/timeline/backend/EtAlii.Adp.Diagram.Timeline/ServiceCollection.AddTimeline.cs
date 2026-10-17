using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
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
    public static void AddTimeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The schema marker and an empty element list, and nothing else (Requirement 1.3): an empty
        // timeline is a valid one, and a placeholder element would be the first thing every user
        // deletes. The base name is not written - a timeline has no title line in its schema. CRLF,
        // the house style, since a new file has no style of its own to keep.
        services.AddEmptyDocument(Diagram.Timeline.Origin, "timeline: 1\r\nelements: []\r\n");
        services.AddSingleton<TimelineElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add
        // so a test that registers its own store keeps it.
        services.TryAddSingleton<ITimelineDocumentStore, TimelineDocumentStore>();

        // The registration file is ignored: a `generic/timeline` `.adp` carries nothing beyond its
        // MIME line, and a bare `.tml` routes here with no registration at all (Requirement 1.2).
        // The project's history, so a drag is one undo away like every other edit.
        services.AddDiagramSession(Diagram.Timeline.Origin, (provider, opening) => new TimelineSession(
            opening.BodyPath,
            provider.GetRequiredService<ITimelineDocumentStore>(),
            provider.GetRequiredService<TimelineElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

        // The reload seam: an external write to a timeline reaches the store, and through it
        // every open session (modular-text-editors Requirement 5.3) - and a deleted body reaches
        // it as a deletion, not as a reload.
        services.AddDiagramDocumentReloader<ITimelineDocumentStore>(Diagram.Timeline.Origin);

        // The commands, one handler each: the whole editable surface of a timeline
        // (Requirement 11.1), and nothing writes the file except through them.
        services.AddSingleton<ICommandHandler<SetTimelinePlacementCommand>, SetTimelinePlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<AddTimelineElementCommand>, AddTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveTimelineElementCommand>, RemoveTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameTimelineElementCommand>, RenameTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectTimelineElementsCommand>, ConnectTimelineElementsCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectTimelineConnectionCommand>, DisconnectTimelineConnectionCommandHandler>();
        services.AddSingleton<ICommandHandler<RelabelTimelineConnectionCommand>, RelabelTimelineConnectionCommandHandler>();
        services.AddSingleton<ICommandHandler<SetTimelineEndCommand>, SetTimelineEndCommandHandler>();
        services.AddSingleton<ICommandHandler<AddConnectedTimelineElementCommand>, AddConnectedTimelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<ArrangeTimelineCommand>, ArrangeTimelineCommandHandler>();
        services.AddSingleton<ICommandHandler<SetTimelineRowsCommand>, SetTimelineRowsCommandHandler>();

        // Every undo: the whole text as it was before the edit, byte for byte (timeline-disl-fbl
        // Requirement 3.6), the shared restore closed over this module's store.
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<ITimelineDocumentStore>>, RestoreDocumentCommandHandler<ITimelineDocumentStore>>();

        // The context seams: selection, actions, properties and the palette, which is the definition's
        // toolbox, each entry naming the add action its drop commits (Requirement 9.2).
        services.AddSingleton<IContextSourceResolver, TimelineContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, TimelineContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, TimelineContextPropertyProvider>();
        services.AddSingleton<IDiagramToolboxProvider>(new DislToolboxProvider(Diagram.Timeline.Origin, TimelineDefinition.Definition));

        // The rules' join to the Errors and Warnings panel.
        services.AddSingleton<IDiagramValidator, TimelineValidator>();
    }
}
