using EtAlii.Adp.Diagram;

using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Registers the whole Azure Pipelines module against core's seams.
/// </summary>
/// <remarks>
/// One call for the module, as <c>AddC4</c> and <c>AddMindmap</c> are for theirs - the host
/// names the module once rather than listing its seams, and a test that needs the real module
/// calls the same method. Core never names the module back: everything resolves by
/// <see cref="DiagramOrigin"/> (Requirement 14.3).
/// </remarks>
public static class ServiceCollectionAddAzurePipelineExtension
{
    public static IServiceCollection AddAzurePipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A type that declares a document extension must be able to write an empty one; the
        // host checks that at startup rather than at the first Add.
        services.AddSingleton<IDiagramDocumentFactory, PipelineDocumentFactory>();

        return services;
    }
}
