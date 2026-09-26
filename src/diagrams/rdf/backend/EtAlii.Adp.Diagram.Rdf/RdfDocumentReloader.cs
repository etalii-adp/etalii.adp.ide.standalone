using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Core's reload seam for the family: when the bridge sees a body or its registration change on
/// disk, the store re-reads it and every open session hears - which is also how a stored
/// reposition reaches the other connections, since the <c>layout:</c> block lives in the
/// registration rather than the body (Requirement 1.7).
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed an empty document. Since task 6 this store keeps the last good document
/// through a reload that cannot read, so a deletion routed to a reload would keep a deleted document
/// on the canvas for good, with nothing failing - for every reading of the family, since they share
/// the one store. The guard is <c>RdfDocumentReloader.Tests</c>, seen to fail with this override
/// removed.
/// </remarks>
public sealed class RdfDocumentReloader(DiagramOrigin origin, IRdfDocumentStore documents) : IDiagramDocumentReloader
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
