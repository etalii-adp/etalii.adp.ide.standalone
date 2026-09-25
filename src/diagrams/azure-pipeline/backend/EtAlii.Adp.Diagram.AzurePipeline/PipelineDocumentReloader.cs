using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Core's reload seam for pipelines: when the bridge sees a pipeline body or its registration
/// change on disk, the store re-reads it - dropping the root's template cache with it, since
/// what changed may be a template - and every open session hears (modular-text-editors
/// Requirement 5.3).
/// </summary>
public sealed class PipelineDocumentReloader : IDiagramDocumentReloader
{
    private readonly IPipelineDocumentStore _documents;

    public PipelineDocumentReloader(IPipelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public DiagramOrigin Origin => Diagram.Pipeline.Origin;

    public void Reload(string rootPath, string bodyPath) => _documents.Reload(rootPath, bodyPath);
}
