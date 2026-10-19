using System.Text;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
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
/// <see cref="ToolOrigin"/>. The family's three MIME types each get their own validator
/// instance over the same rule set, because the seam resolves by origin.
/// </remarks>
public static class ServiceCollectionAddDatabricksExtension
{
    /// <summary>The family's three origins, as docs/tools.md writes them.</summary>
    public static readonly ToolOrigin BundleOrigin = new("databricks", "bundle");

    /// <inheritdoc cref="BundleOrigin" />
    public static readonly ToolOrigin JobOrigin = new("databricks", "job");

    /// <inheritdoc cref="BundleOrigin" />
    public static readonly ToolOrigin PipelineOrigin = new("databricks", "pipeline");

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

        // The action and property seams, once for the whole family: which of the three types a
        // file is does not change what an element is (the C4 precedent).
        services.AddSingleton<IContextActionProvider, DatabricksContextActionProvider>();
        services.AddSingleton<IContextPropertyProvider, DatabricksContextPropertyProvider>();

        foreach (var origin in (ToolOrigin[])[BundleOrigin, JobOrigin, PipelineOrigin])
        {
            // The element resolver, one per type, because the shared base answers for the files of
            // one type; what it says of an element is the family's, whichever type it is.
            services.AddSingleton<IContextSourceResolver>(provider => new DatabricksContextSourceResolver(
                origin,
                provider.GetRequiredService<DiagramFileRouter>(),
                provider.GetRequiredService<IDatabricksDocumentStore>(),
                provider.GetRequiredService<DatabricksElementMapper>()));

            // The palette this type offers: entries name actions above rather than carrying an
            // implementation, so a drop and a menu click are the same edit (Requirement 9).
            services.AddSingleton<IDiagramToolboxProvider>(_ => new DatabricksToolboxProvider(origin));

            // The empty body a new diagram of this type starts as - the one thing core cannot
            // derive from the definition on its own. Written from the file's name, which its keys
            // are made from.
            services.AddEmptyDocument(origin, baseName => EmptyDocument(origin.Type, baseName));

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
                provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(opening.RootPath))));

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

    /// <summary>
    /// The minimal document a new diagram of the family's <paramref name="type"/> starts as -
    /// complete enough to open, parse and validate clean, because a skeleton that opens with
    /// findings would be a refusal factory.
    /// </summary>
    /// <remarks>
    /// CRLF and a trailing newline: the writers preserve whatever a file already uses (Requirement
    /// 2.1), but a file ADP creates has no existing style to preserve, and CRLF is the repository's
    /// own house style.
    /// </remarks>
    public static string EmptyDocument(string type, string baseName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(baseName);

        var key = KeyOf(baseName);
        return type switch
        {
            "job" =>
                "resources:\r\n"
                + "  jobs:\r\n"
                + $"    {key}:\r\n"
                + $"      name: {baseName}\r\n"
                + "      tasks:\r\n"
                + "        - task_key: main\r\n"
                + "          notebook_task:\r\n"
                + $"            notebook_path: notebooks/{key}\r\n",
            "pipeline" =>
                "{\r\n"
                + $"  \"name\": \"{key}\",\r\n"
                + "  \"libraries\": [\r\n"
                + $"    {{ \"notebook\": {{ \"path\": \"transformations/{key}\" }} }}\r\n"
                + "  ]\r\n"
                + "}\r\n",
            _ =>
                "bundle:\r\n"
                + $"  name: {key}\r\n"
                + "targets:\r\n"
                + "  dev:\r\n"
                + "    mode: development\r\n"
                + "    default: true\r\n",
        };
    }

    /// <summary>
    /// The base name as a Databricks key: lowercased, with anything a key cannot carry folded
    /// to underscores - so "Nightly Ingest.job" starts life as <c>nightly_ingest_job</c>.
    /// </summary>
    private static string KeyOf(string baseName)
    {
        var builder = new StringBuilder(baseName.Length);
        foreach (var character in baseName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : '_');
        }

        var key = builder.ToString().Trim('_');
        return key.Length > 0 ? key : "untitled";
    }
}
