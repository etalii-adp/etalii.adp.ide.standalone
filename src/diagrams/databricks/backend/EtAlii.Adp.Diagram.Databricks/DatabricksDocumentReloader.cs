using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Core's reload seam for the family: when the bridge sees a body or its registration change on
/// disk, the store re-reads it and every open session hears - which is also how a stored
/// reposition reaches the other connections, since the <c>layout:</c> block lives in the
/// registration rather than the body.
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed an empty document. Since task 6 this store keeps the last good document
/// through a reload that cannot read, so a deletion routed to a reload would keep a deleted file on
/// the canvas for good, with nothing failing. The guard is <c>DatabricksDocumentReloader.Tests</c>,
/// seen to fail with this override removed.
/// </remarks>
public sealed class DatabricksDocumentReloader(DiagramOrigin origin, IDatabricksDocumentStore documents) : IDiagramDocumentReloader
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
