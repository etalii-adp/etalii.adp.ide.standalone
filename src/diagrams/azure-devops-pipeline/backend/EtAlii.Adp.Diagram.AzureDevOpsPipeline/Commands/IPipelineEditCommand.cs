using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>A command that edits a pipeline, which is read from a workspace root as well as a path.</summary>
public interface IPipelineEditCommand : IDocumentEditCommand
{
    /// <summary>The project, which the store needs to resolve templates.</summary>
    ProjectRoot Root { get; }
}
