using EtAlii.Adp.Backend.Diagrams;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Core's reload seam for mindmaps: when the bridge sees a <c>.mm</c> body or its registration
/// change on disk, the store re-reads it and every open session hears (modular-text-editors
/// Requirement 5.3, mindmap-diagram Requirement 11.8).
/// </summary>
public sealed class MindmapDocumentReloader : IDiagramDocumentReloader
{
    private readonly IMindmapDocumentStore _documents;

    public MindmapDocumentReloader(IMindmapDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public DiagramOrigin Origin => Diagram.Mindmap.Origin;

    public void Reload(string rootPath, string bodyPath) => _documents.Reload(bodyPath);
}
