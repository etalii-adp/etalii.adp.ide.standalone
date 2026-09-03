using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
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
    /// <summary>The family's three origins, as docs/diagrams.md writes them.</summary>
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

        foreach (var origin in (DiagramOrigin[])[BundleOrigin, JobOrigin, PipelineOrigin])
        {
            // The empty body a new diagram of this type starts as - the one thing core cannot
            // derive from the definition on its own.
            services.AddSingleton<IDiagramDocumentFactory>(_ => new DatabricksDocumentFactory(origin));

            // The session seam: which declaration a session shows comes from the .adp file's
            // resource: header, so all three factories are the same code under different origins.
            services.AddSingleton<IDiagramSessionFactory>(provider => new DatabricksSessionFactory(
                origin,
                provider.GetRequiredService<IDatabricksDocumentStore>(),
                provider.GetRequiredService<DatabricksElementMapper>(),
                provider.GetRequiredService<IHistoryStackStore>()));

            // The reload seam: an external write to a body OR its registration reaches the
            // shared store, and through it every open session - which is how a stored
            // reposition in the layout: block reaches the other connections.
            services.AddSingleton<IDiagramDocumentReloader>(provider => new DatabricksDocumentReloader(
                origin,
                provider.GetRequiredService<IDatabricksDocumentStore>()));

            // The rules, resolved by origin through core's validator registry, so the family's
            // problems reach the Errors and Warnings panel like any other type's (Requirement 12).
            services.AddSingleton<IDiagramValidator>(_ => new DatabricksValidator(origin));
        }

        return services;
    }
}
