using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>This diagram type's identity, cataloged in docs/diagrams.md as `azure-devops/pipeline`.</summary>
public static class Diagram
{
    /// <summary>
    /// The extension an Azure Pipelines document carries. Declared <b>shared</b>: a repository
    /// is full of workflows, compose files and manifests that end in `.yml` and are not
    /// pipelines, so this type never claims one on sight. A file becomes a pipeline diagram when
    /// the user registers it (Requirements 2.1-2.3).
    /// </summary>
    public const string DocumentExtension = ".yml";

    public static DiagramDefinition[] Definitions { get; } =
    [
        new(
            new DiagramOrigin("azure-devops", "pipeline"),
            "Azure DevOps pipeline",
            "A CI/CD pipeline's stages, jobs and steps, and which of them wait for which.",
            Extension: DocumentExtension,
            SharedExtension: true),
    ];
}
