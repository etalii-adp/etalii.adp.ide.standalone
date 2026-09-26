using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Core's reload seam for Wardley maps: when the bridge sees an <c>.owm</c> body or its
/// registration change on disk, the store re-reads it and every open session hears
/// (modular-text-editors Requirement 5.3).
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed an empty document. Since task 6 this store keeps the last good document
/// through a reload that cannot read, so a deletion routed to a reload would keep a deleted map on
/// the canvas for good, with nothing failing. The guard is <c>WardleyDocumentReloader.Tests</c>,
/// seen to fail with this override removed.
/// </remarks>
public sealed class WardleyDocumentReloader : IDiagramDocumentReloader
{
    private readonly IWardleyDocumentStore _documents;

    public WardleyDocumentReloader(IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    public DiagramOrigin Origin => Diagram.WardleyMap.Origin;

    public void Reload(string rootPath, string bodyPath) => _documents.Reload(bodyPath);

    public void BodyDeleted(string rootPath, string bodyPath) => _documents.BodyDeleted(bodyPath);
}
