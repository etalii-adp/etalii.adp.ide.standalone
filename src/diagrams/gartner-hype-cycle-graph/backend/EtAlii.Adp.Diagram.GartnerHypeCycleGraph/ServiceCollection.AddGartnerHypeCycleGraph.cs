using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

public static class ServiceCollectionAddGartnerHypeCycleGraphExtension
{
    /// <summary>
    /// Registers what a hype cycle graph needs to open, to follow its file, to report its breaches and
    /// to be edited: the store, the mapper, the session factory, the reload seam, the validator, the
    /// selection resolver, the toolbox, action and property providers, and one handler per command.
    /// </summary>
    public static void AddGartnerHypeCycleGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Without a document factory the host refuses to start: a type that declares an extension
        // must be creatable from the Add dialog.
        services.AddEmptyDocument(Diagram.HypeCycleGraph.Origin, GhgParser.EmptyDocument("\r\n"));
        services.AddSingleton<GhgElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add so a
        // test that registers its own store keeps it.
        services.TryAddSingleton<IGhgDocumentStore, GhgDocumentStore>();

        // The project's history, so a drag is one undo away like every other edit.
        services.AddDiagramSession(Diagram.HypeCycleGraph.Origin, (provider, opening) => new GhgSession(
            opening.BodyPath,
            provider.GetRequiredService<IGhgDocumentStore>(),
            provider.GetRequiredService<GhgElementMapper>(),
            provider.GetRequiredService<IHistoryStackStore>().Get(opening.Root)));

        // The reload seam: an external write to a .ghg body reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddDiagramDocumentReloader<IGhgDocumentStore>(Diagram.HypeCycleGraph.Origin);

        // Every breach, to the Errors and Warnings panel (Requirement 2.4).
        services.AddSingleton<IDiagramValidator, GhgValidator>();

        // Selection: a trend or influence of a .ghg resolves to a selection the panels read.
        services.AddSingleton<IContextSourceResolver, GhgContextSourceResolver>();

        // What can be done to it: the palette, the menus and shortcuts, and the property grid.
        services.AddSingleton<IDiagramToolboxProvider>(new DislToolboxProvider(Diagram.HypeCycleGraph.Origin, GhgDefinition.Definition));
        services.AddSingleton<IContextActionProvider, GhgContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, GhgContextPropertyProvider>();

        // The commands, one handler each: the whole editable surface, and nothing writes the file
        // except through them. Every one's undo is the shared restore command, registered once.
        services.AddSingleton<ICommandHandler<AddGhgTrendCommand>, AddGhgTrendCommandHandler>();
        services.AddSingleton<ICommandHandler<AddGhgTriggerCommand>, AddGhgTriggerCommandHandler>();
        services.AddSingleton<ICommandHandler<AddGhgNoteCommand>, AddGhgNoteCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgNoteSizeCommand>, SetGhgNoteSizeCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveGhgElementCommand>, RemoveGhgElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgPlacementCommand>, SetGhgPlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgSpanCommand>, SetGhgSpanCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgBoundaryCommand>, SetGhgBoundaryCommandHandler>();
        services.AddSingleton<ICommandHandler<ClearGhgBoundariesCommand>, ClearGhgBoundariesCommandHandler>();
        services.AddSingleton<ICommandHandler<ArrangeGhgCommand>, ArrangeGhgCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgPhasesCommand>, SetGhgPhasesCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameGhgElementCommand>, RenameGhgElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgTagsCommand>, SetGhgTagsCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgDescriptionCommand>, SetGhgDescriptionCommandHandler>();
        services.AddSingleton<ICommandHandler<AddGhgInfluenceCommand>, AddGhgInfluenceCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgAttachmentCommand>, SetGhgAttachmentCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveGhgInfluenceCommand>, RemoveGhgInfluenceCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IGhgDocumentStore>>, RestoreDocumentCommandHandler<IGhgDocumentStore>>();
    }
}
