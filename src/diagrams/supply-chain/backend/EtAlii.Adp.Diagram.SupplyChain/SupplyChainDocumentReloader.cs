using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The reload seam: an external write to a <c>.supply</c> body reaches the store, and through it
/// every open session.
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> is overridden on purpose.</b> The default routes a deletion to a
/// reload, which this store answers by keeping the last good document - so a deleted diagram would
/// stay on the canvas. The lifecycle's own deletion is the one call that clears it.
/// </remarks>
public sealed class SupplyChainDocumentReloader : IDiagramDocumentReloader
{
    private readonly ISupplyChainDocumentStore _documents;

    public SupplyChainDocumentReloader(ISupplyChainDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        _documents.Reload(bodyPath);
    }

    /// <inheritdoc />
    public void BodyDeleted(string rootPath, string bodyPath)
    {
        _ = rootPath;
        _documents.BodyDeleted(bodyPath);
    }
}
