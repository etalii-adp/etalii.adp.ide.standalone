using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Core's reload seam: when the bridge sees a query or its registration change on disk, the
/// store re-reads it and every open session hears. That path matters more here than elsewhere -
/// the text editor is where these files are edited, so an external change is the normal way a
/// query changes at all (Requirement 1.5).
/// </summary>
public sealed class SparqlDocumentReloader(DiagramOrigin origin, ISparqlDocumentStore documents) : IDiagramDocumentReloader
{
    public DiagramOrigin Origin { get; } = origin;

    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        documents.Reload(bodyPath);
    }
}
