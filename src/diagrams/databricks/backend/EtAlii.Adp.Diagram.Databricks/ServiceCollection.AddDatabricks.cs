using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Registers the Databricks module against core's seams.
/// </summary>
/// <remarks>
/// One call for the module, as <c>AddAzurePipeline</c> and <c>AddTimeline</c> are for theirs -
/// the host names the module once rather than listing its seams, and a test that needs the real
/// module calls the same method. Core never names the module back: everything resolves by
/// <see cref="DiagramOrigin"/>. The family's three MIME types each get their own validator
/// instance over the same rule set, because the seam resolves by origin.
/// </remarks>
public static class ServiceCollectionAddDatabricksExtension
{
    /// <summary>The family's three origins, as docs/tools.md writes them.</summary>
    public static readonly DiagramOrigin BundleOrigin = new("databricks", "bundle");

    /// <inheritdoc cref="BundleOrigin" />
    public static readonly DiagramOrigin JobOrigin = new("databricks", "job");

    /// <inheritdoc cref="BundleOrigin" />
    public static readonly DiagramOrigin PipelineOrigin = new("databricks", "pipeline");

    public static IServiceCollection AddDatabricks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One store and one mapper for the process: the family's three diagram types on one
        // file have to share its document, or an edit through either would be invisible to the
        // others. TryAdd, so a test that registered its own first keeps it.
        services.TryAddSingleton<IDatabricksDocumentStore, DatabricksDocumentStore>();
        services.TryAddSingleton<DatabricksElementMapper>();

        // The family's commands, so every real edit is one undo away (tech.md's Commands rule).
        services.AddSingleton<ICommandHandler<InsertDatabricksTaskCommand>, InsertDatabricksTaskCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveDatabricksTaskCommand>, RemoveDatabricksTaskCommandHandler>();
        services.AddSingleton<ICommandHandler<ConnectDatabricksTasksCommand>, ConnectDatabricksTasksCommandHandler>();
        services.AddSingleton<ICommandHandler<DisconnectDatabricksTasksCommand>, DisconnectDatabricksTasksCommandHandler>();
        services.AddSingleton<ICommandHandler<RenameDatabricksTaskCommand>, RenameDatabricksTaskCommandHandler>();
        services.AddSingleton<ICommandHandler<SetDatabricksRunIfCommand>, SetDatabricksRunIfCommandHandler>();
        services.AddSingleton<ICommandHandler<SetDatabricksClusterCommand>, SetDatabricksClusterCommandHandler>();
        services.AddSingleton<ICommandHandler<AddDatabricksResourceCommand>, AddDatabricksResourceCommandHandler>();
        services.AddSingleton<ICommandHandler<SetDatabricksBundleNameCommand>, SetDatabricksBundleNameCommandHandler>();
        services.AddSingleton<ICommandHandler<InsertDatabricksLibraryCommand>, InsertDatabricksLibraryCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveDatabricksLibraryCommand>, RemoveDatabricksLibraryCommandHandler>();
        services.AddSingleton<ICommandHandler<SetDatabricksPipelineScalarCommand>, SetDatabricksPipelineScalarCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreDocumentCommand<IDatabricksDocumentStore>>, RestoreDocumentCommandHandler<IDatabricksDocumentStore>>();

        // The context seams, once for the whole family: which of the three types a file is does
        // not change what an element is (the C4 precedent).
        services.AddSingleton<IContextSourceResolver, DatabricksContextSourceResolver>();
        services.AddSingleton<IContextActionProvider, DatabricksContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, DatabricksContextPropertyProvider>();

        foreach (var origin in (DiagramOrigin[])[BundleOrigin, JobOrigin, PipelineOrigin])
        {
            // The palette this type offers: entries name actions above rather than carrying an
            // implementation, so a drop and a menu click are the same edit (Requirement 9).
            services.AddSingleton<IDiagramToolboxProvider>(_ => new DatabricksToolboxProvider(origin));

            // The empty body a new diagram of this type starts as - the one thing core cannot
            // derive from the definition on its own. A class rather than AddEmptyDocument: its
            // keys are made from the file's name, so its text is not the same for every file.
            services.AddSingleton<IDiagramDocumentFactory>(_ => new DatabricksDocumentFactory(origin));

            // The session seam: which declaration a session shows comes from the .adp file's
            // resource: header, so all three types open the same session under different origins.
            // The project's history, so a drag on the canvas is one undo away like every other edit.
            services.AddDiagramSession(origin, (provider, opening) => new DatabricksSession(
                opening.BodyPath,
                opening.RegistrationPath,
                origin,
                DatabricksHeaders.ReadResourceKey(opening.RegistrationPath),
                provider.GetRequiredService<IDatabricksDocumentStore>(),
                provider.GetRequiredService<DatabricksElementMapper>(),
                provider.GetRequiredService<IHistoryStackStore>().Get(opening.RootPath)));

            // The reload seam: an external write to a body OR its registration reaches the
            // shared store, and through it every open session - which is how a stored
            // reposition in the layout: block reaches the other connections.
            services.AddDiagramDocumentReloader<IDatabricksDocumentStore>(origin);

            // The rules, resolved by origin through core's validator registry, so the family's
            // problems reach the Errors and Warnings panel like any other type's (Requirement 12).
            services.AddSingleton<IDiagramValidator>(_ => new DatabricksValidator(origin));
        }

        return services;
    }
}
