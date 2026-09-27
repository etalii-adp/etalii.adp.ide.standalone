using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
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
    public static IServiceCollection AddGartnerHypeCycleGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Without a document factory the host refuses to start: a type that declares an extension
        // must be creatable from the Add dialog.
        services.AddSingleton<IDiagramDocumentFactory, GhgDocumentFactory>();
        services.AddSingleton<GhgElementMapper>();

        // One document per path, shared by every connection viewing it. TryAdd rather than Add so a
        // test that registers its own store keeps it.
        services.TryAddSingleton<IGhgDocumentStore, GhgDocumentStore>();

        services.AddSingleton<IDiagramSessionFactory, GhgSessionFactory>();

        // The reload seam: an external write to a .ghg body reaches the store, and through it every
        // open session - and a deleted body reaches it as a deletion, not as a reload.
        services.AddSingleton<IDiagramDocumentReloader, GhgDocumentReloader>();

        // Every breach, to the Errors and Warnings panel (Requirement 2.4).
        services.AddSingleton<IDiagramValidator, GhgValidator>();

        // Selection: a trend or influence of a .ghg resolves to a selection the panels read.
        services.AddSingleton<IContextSourceResolver, GhgContextSourceResolver>();

        // What can be done to it: the palette, the menus and shortcuts, and the property grid.
        services.AddSingleton<IDiagramToolboxProvider, GhgToolboxProvider>();
        services.AddSingleton<IContextActionProvider, GhgContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, GhgContextPropertyProvider>();

        // The commands, one handler each: the whole editable surface, and nothing writes the file
        // except through them. Every one's undo is the shared restore command, registered once.
        services.AddSingleton<ICommandHandler<AddGhgTrendCommand>, AddGhgTrendCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveGhgTrendCommand>, RemoveGhgTrendCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgPlacementCommand>, SetGhgPlacementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgSpanCommand>, SetGhgSpanCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgBoundaryCommand>, SetGhgBoundaryCommandHandler>();
        services.AddSingleton<ICommandHandler<ClearGhgBoundariesCommand>, ClearGhgBoundariesCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgPhasesCommand>, SetGhgPhasesCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameGhgTrendCommand>, RenameGhgTrendCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgTagsCommand>, SetGhgTagsCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgDescriptionCommand>, SetGhgDescriptionCommandHandler>();
        services.AddSingleton<ICommandHandler<AddGhgInfluenceCommand>, AddGhgInfluenceCommandHandler>();
        services.AddSingleton<ICommandHandler<SetGhgAttachmentCommand>, SetGhgAttachmentCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveGhgInfluenceCommand>, RemoveGhgInfluenceCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IGhgDocumentStore>>, RestoreDocumentCommandHandler<IGhgDocumentStore>>();

        return services;
    }
}
