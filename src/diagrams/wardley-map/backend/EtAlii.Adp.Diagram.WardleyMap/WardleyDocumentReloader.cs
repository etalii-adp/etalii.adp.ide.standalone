using EtAlii.Adp.Backend.Diagrams;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Core's reload seam for Wardley maps: when the bridge sees an <c>.owm</c> body or its
/// registration change on disk, the store re-reads it and every open session hears
/// (modular-text-editors Requirement 5.3).
/// </summary>
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
}
