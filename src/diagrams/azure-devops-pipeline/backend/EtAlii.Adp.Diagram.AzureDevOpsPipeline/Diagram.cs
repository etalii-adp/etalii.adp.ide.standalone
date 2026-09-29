using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `azure-devops/pipeline`.</summary>
public static class Diagram
{
    /// <summary>
    /// The extension an Azure Pipelines document carries. Declared <b>shared</b>: a repository
    /// is full of workflows, compose files and manifests that end in `.yml` and are not
    /// pipelines, so this type never claims one on sight. A file becomes a pipeline diagram when
    /// the user registers it (Requirements 2.1-2.3).
    /// </summary>
    public const string DocumentExtension = ".yml";

    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition Pipeline { get; } = new(
        new DiagramOrigin("azure-devops", "pipeline"),
        "Azure DevOps pipeline",
        "A CI/CD pipeline's stages, jobs and steps, and which of them wait for which.",
        Icon: "mdi-rocket-launch-outline",
        Extension: DocumentExtension,
        SharedExtension: true,
        // Azure DevOps pipelines: a document the repository already owns, registered by the user
        // rather than routed on sight, because .yml belongs to no one type.
        Build: builder => builder.Services.AddAzurePipeline());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [Pipeline];
}
