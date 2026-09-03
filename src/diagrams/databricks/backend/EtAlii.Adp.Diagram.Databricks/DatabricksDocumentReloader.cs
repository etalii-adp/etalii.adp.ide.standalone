using EtAlii.Adp.Backend.Diagrams;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Core's reload seam for the family: when the bridge sees a body or its registration change on
/// disk, the store re-reads it and every open session hears - which is also how a stored
/// reposition reaches the other connections, since the <c>layout:</c> block lives in the
/// registration rather than the body.
/// </summary>
public sealed class DatabricksDocumentReloader(DiagramOrigin origin, IDatabricksDocumentStore documents) : IDiagramDocumentReloader
{
    public DiagramOrigin Origin { get; } = origin;

    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        documents.Reload(bodyPath);
    }
}
