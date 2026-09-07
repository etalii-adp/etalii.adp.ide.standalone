using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Core's reload seam for one C4 type: when the bridge sees a C4 body or registration change
/// on disk, the shared store re-reads it and every session hears (modular-text-editors
/// Requirement 5.3). Six of these are registered, one per origin, over the one store - the
/// same shape the session factories have.
/// </summary>
public sealed class C4DocumentReloader : IDiagramDocumentReloader
{
    private readonly IC4DocumentStore _documents;

    public C4DocumentReloader(DiagramOrigin origin, IC4DocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);

        Origin = origin;
        _documents = documents;
    }

    public DiagramOrigin Origin { get; }

    public void Reload(string rootPath, string bodyPath) => _documents.Reload(bodyPath);
}
