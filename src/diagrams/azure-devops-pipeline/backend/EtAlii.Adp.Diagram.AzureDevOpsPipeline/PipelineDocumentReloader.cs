using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Core's reload seam for pipelines: when the bridge sees a pipeline body or its registration
/// change on disk, the store re-reads it - dropping the root's template cache with it, since
/// what changed may be a template - and every open session hears (modular-text-editors
/// Requirement 5.3).
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed an empty document. Since task 6 this store keeps the last good document
/// through a reload that cannot read, so a deletion routed to a reload would keep a deleted pipeline on
/// the canvas for good, with nothing failing. The guard is <c>PipelineDocumentReloader.Tests</c>, seen
/// to fail with this override removed.
/// </remarks>
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

    public void BodyDeleted(string rootPath, string bodyPath) => _documents.BodyDeleted(rootPath, bodyPath);
}
