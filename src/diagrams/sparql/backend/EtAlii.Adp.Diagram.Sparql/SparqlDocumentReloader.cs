using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Core's reload seam: when the bridge sees a query or its registration change on disk, the
/// store re-reads it and every open session hears. That path matters more here than elsewhere -
/// the text editor is where these files are edited, so an external change is the normal way a
/// query changes at all (Requirement 1.5).
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed the "does not exist" entry. Since task 7 this store keeps the last good
/// query through a reload that cannot read, so a deletion routed to a reload would keep a deleted query
/// on the canvas for good, with nothing failing. The guard is <c>SparqlDocumentReloader.Tests</c>, seen
/// to fail with this override removed.
/// </remarks>
public sealed class SparqlDocumentReloader(DiagramOrigin origin, ISparqlDocumentStore documents) : IDiagramDocumentReloader
{
    public DiagramOrigin Origin { get; } = origin;

    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        documents.Reload(bodyPath);
    }

    public void BodyDeleted(string rootPath, string bodyPath)
    {
        _ = rootPath;
        documents.BodyDeleted(bodyPath);
    }
}
