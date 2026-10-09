using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The reload seam: a write to an activity file from outside - an agent's, most of the time -
/// reaches the store, and through it every open session (Requirement 8.1).
/// </summary>
public sealed class AadDocumentReloader : IDiagramDocumentReloader
{
    private readonly IAadDocumentStore _documents;

    public AadDocumentReloader(IAadDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public DiagramOrigin Origin => Diagram.AgentActivity.Origin;

    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        _documents.Reload(bodyPath);
    }

    public void BodyDeleted(string rootPath, string bodyPath)
    {
        _ = rootPath;
        _documents.BodyDeleted(bodyPath);
    }
}
