namespace EtAlii.Adp.Diagram.AzurePipeline;

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
public sealed class PipelineDocumentFactory : IDiagramDocumentFactory
{
    public DiagramOrigin Origin { get; } = Diagram.Definitions[0].Origin;

    public string CreateEmptyDocument(string baseName) =>
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
