using Microsoft.Extensions.DependencyInjection;

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

        // One store for the process: the family's three diagram types on one file have to share
        // its document, or an edit through either would be invisible to the others.
        services.AddSingleton<IDatabricksDocumentStore, DatabricksDocumentStore>();

        // The rules. Registered like any other seam, so the family's problems reach the Errors
        // and Warnings panel exactly as every other type's do (Requirement 12).
        services.AddSingleton<IDiagramValidator>(new DatabricksValidator(BundleOrigin));
        services.AddSingleton<IDiagramValidator>(new DatabricksValidator(JobOrigin));
        services.AddSingleton<IDiagramValidator>(new DatabricksValidator(PipelineOrigin));

        return services;
    }
}
