using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Core's reload seam for dependency graphs: when the bridge sees a graph body or its
/// registration change on disk, the store re-reads it and every open session hears.
/// </summary>
public sealed class DependencyGraphDocumentReloader : IDiagramDocumentReloader
{
    private readonly IDependencyGraphDocumentStore _documents;

    public DependencyGraphDocumentReloader(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    public void Reload(string rootPath, string bodyPath) => _documents.Reload(bodyPath);
}
