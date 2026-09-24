using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Where the commands reach a mindmap by its body path. One loaded document per open
/// diagram, so two commands on the same map - or two connections viewing it - share one tree
/// and one history of changes (Requirement 2.4).
/// </summary>
public interface IMindmapDocumentStore
{
    /// <summary>
    /// The document at <paramref name="bodyPath"/>, loading it on first ask. A missing body
    /// opens as an empty map named after the file (Requirement 2.5).
    /// </summary>
    /// <exception cref="MindmapFormatException">The file exists but is not a Freeplane map.</exception>
    MindmapDocument GetOrLoad(string bodyPath);

    /// <summary>The loaded document, or null when nothing has opened it; never loads.</summary>
    MindmapDocument? Get(string bodyPath);

    /// <summary>Writes the loaded document back to <paramref name="bodyPath"/>, atomically, and reports the change.</summary>
    /// <remarks>
    /// <b>Returns a result rather than throwing for a failed write, and the edit stays in memory when
    /// it fails</b> (backend-centralization R3.2, R3.4). It used to be <c>void</c> and let the
    /// writer's exception escape, so a refused write reached the caller as an exception rather than a
    /// sentence and the user was told nothing they could act on. A save this store never loaded a
    /// document for is still an exception: that is a programming error, not an outcome.
    /// </remarks>
    DocumentSaveResult Save(string bodyPath, MindmapChange change);

    /// <summary>
    /// Re-reads a map an external tool changed on disk and announces it (Requirement 11.8).
    /// A no-op for a map nothing loaded, and while the store's own save of that path is in
    /// flight - its own write is not an external change, and must not bounce back as one.
    /// </summary>
    void Reload(string bodyPath);

    /// <summary>Raised after every save, with what changed, so the open connections can be told.</summary>
    event EventHandler<MindmapChangedEventArgs>? Changed;
}
