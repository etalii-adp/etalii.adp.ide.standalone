using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Registers the whole Azure Pipelines module against core's seams.
/// </summary>
/// <remarks>
/// One call for the module, as <c>AddC4</c> and <c>AddMindmap</c> are for theirs - the host
/// names the module once rather than listing its seams, and a test that needs the real module
/// calls the same method. Core never names the module back: everything resolves by
/// <see cref="ToolOrigin"/> (Requirement 14.3).
/// </remarks>
public static class ServiceCollectionAddAzurePipelineExtension
{
    public static void AddAzurePipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A type that declares a document extension must be able to write an empty one; the
        // host checks that at startup rather than at the first Add. Written from the file's name,
        // which the new pipeline's first comment carries.
        services.AddEmptyDocument(Diagram.Pipeline.Origin, NewPipeline);

        // One store for the process: two diagrams on one pipeline have to share its document, or
        // an edit through either would be invisible to the other until a reload.
        services.AddSingleton<IPipelineDocumentStore, PipelineDocumentStore>();

        // The pitch the layout arranges at, as a singleton so the canvas and the backend cannot
        // end up disagreeing about how big a stage is.
        services.AddSingleton(PipelineMetrics.Default);
        services.AddSingleton<PipelineElementMapper>();

        // What each connection has open, shared by the session that owns a stream and the action
        // provider a user reaches - the two never meet otherwise (Requirement 8.2).
        services.AddSingleton<PipelineViewState>();

        services.AddDiagramSession(Diagram.Pipeline.Origin, (provider, opening) => new PipelineSession(
            opening.WatchId,
            opening.RootPath,
            opening.BodyPath,
            provider.GetRequiredService<IPipelineDocumentStore>(),
            provider.GetRequiredService<PipelineElementMapper>(),
            provider.GetRequiredService<PipelineViewState>()));

        // The reload seam: an external write to a pipeline reaches the store, and through it
        // every open session (modular-text-editors Requirement 5.3).
        services.AddDiagramDocumentReloader<IPipelineDocumentStore>(Diagram.Pipeline.Origin);

        // Making an element selectable is registering this and nothing else; the context service
        // is untouched (Requirement 12.2).
        services.AddSingleton<IContextSourceResolver, PipelineContextSourceResolver>();

        // What can be done to an element, offered once and reaching the ribbon, the context menu
        // and the keyboard through the same path (Requirement 9.7).
        services.AddSingleton<IContextActionProvider, PipelineContextActionProvider>();

        // What can be dragged onto the canvas. Each entry names an action above rather than
        // carrying an implementation, so a drop and a menu click are the same edit.
        services.AddSingleton<IDiagramToolboxProvider, PipelineToolboxProvider>();

        // What a selected element shows, and which of it may be written from the panel
        // (Requirement 13.1). Almost everything is shown and not editable, on purpose.
        services.AddSingleton<IContextPropertyProvider, PipelineContextPropertyProvider>();

        // The rules. Registered like any other seam, so its problems reach the Errors and
        // Warnings panel exactly as every other type's do (Requirement 10.1).
        services.AddSingleton<IDiagramValidator, PipelineValidator>();

        // The editable set, one handler per command. Every one of them is undoable, because every
        // one reports the command that reverses it (Requirement 9.2).
        services.AddSingleton<ICommandHandler<RenamePipelineElementCommand>, RenamePipelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<SetPipelineDependenciesCommand>, SetPipelineDependenciesCommandHandler>();
        services.AddSingleton<ICommandHandler<SetPipelineElementEnabledCommand>, SetPipelineElementEnabledCommandHandler>();
        services.AddSingleton<ICommandHandler<AddPipelineElementCommand>, AddPipelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RemovePipelineElementCommand>, RemovePipelineElementCommandHandler>();
        services.AddSingleton<ICommandHandler<RestorePipelineLinesCommand>, RestorePipelineLinesCommandHandler>();
        services.AddSingleton<ICommandHandler<MovePipelineStepCommand>, MovePipelineStepCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IPipelineDocumentStore>>, RestoreDocumentCommandHandler<IPipelineDocumentStore>>();
    }

    /// <summary>
    /// The body written when a pipeline is created from the Add dialog: a minimal pipeline that
    /// actually runs.
    /// </summary>
    /// <remarks>
    /// Not an empty file, and not commented-out scaffolding. An Azure Pipelines document that does
    /// not run is not a pipeline - Azure DevOps rejects it - so the smallest useful thing to create
    /// is a trigger, a pool and one job with one step (Requirement 1.3). A user who wanted an empty
    /// file would have made one; a user who chose "Azure DevOps pipeline" wants something that
    /// builds.
    /// <para>
    /// Registering an existing file takes a different path entirely and never calls this: there the
    /// body is the file being registered, and only the <c>.adp</c> is written.
    /// </para>
    /// </remarks>
    private static string NewPipeline(string baseName) =>
        $"""
        # {baseName}

        trigger:
          - main

        pool:
          vmImage: ubuntu-latest

        jobs:
          - job: Build
            displayName: Build
            steps:
              - script: echo "Replace this with your build"
                displayName: Build

        """;
}
