using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Where the commands reach a mindmap by its body path. One loaded document per open
/// diagram, so two commands on the same map - or two connections viewing it - share one tree
/// and one history of changes (Requirement 2.4).
/// </summary>
/// <remarks>
/// Reloading, a deleted body and forgetting are the shared contract's
/// (<see cref="IDiagramDocumentStore"/>), so the shared reloader serves this store. The rest stays the
/// module's own, not <see cref="IDiagramDocumentStore{TBody, TModel}"/> (code-structure FR-029): a map
/// is one mutable tree rather than a body and a model read from it, and its change event names the
/// kind of structural change so a session lays out only what moved.
/// </remarks>
public interface IMindmapDocumentStore : IDiagramDocumentStore
{
    /// <summary>
    /// The document at <paramref name="bodyPath"/>, loading it on first ask without blocking a thread
    /// on the read. A missing body opens as an empty map named after the file (Requirement 2.5).
    /// </summary>
    /// <exception cref="MindmapFormatException">The file exists but is not a Freeplane map.</exception>
    ValueTask<MindmapDocument> GetOrLoadAsync(string bodyPath, CancellationToken cancellationToken);

    /// <summary>The loaded document, or null when nothing has opened it; never loads.</summary>
    MindmapDocument? Get(string bodyPath);

    /// <summary>Writes <paramref name="document"/> back to <paramref name="bodyPath"/>, atomically, and reports the change.</summary>
    /// <remarks>
    /// <b>Returns a result rather than throwing for a failed write, and the edit stays in memory when
    /// it fails</b> (backend-centralization R3.2, R3.4). It used to be <c>void</c> and let the
    /// writer's exception escape, so a refused write reached the caller as an exception rather than a
    /// sentence and the user was told nothing they could act on. A save this store never loaded a
    /// document for is still an exception: that is a programming error, not an outcome.
    /// </remarks>
    /// <param name="bodyPath">The path of the document to write.</param>
    /// <param name="document">
    /// The document to write - **the one the caller edited**, passed in rather than looked up again.
    /// The change argument beside it made this signature look as though it already did that; it
    /// does not, it names what to ANNOUNCE. A save that re-fetched from the cache lost the edit
    /// whenever a reload landed between the edit and the save, and reported success for it.
    /// </param>
    /// <param name="change">What to tell listeners changed - not what to write.</param>
    /// <param name="cancellationToken">Gives up a wait for another save of the map; nothing is then written or cached.</param>
    Task<DocumentSaveResult> SaveAsync(string bodyPath, MindmapDocument document, MindmapChange change, CancellationToken cancellationToken);

    /// <summary>
    /// Raised after every save, with what changed, so the open connections can be told; and after a
    /// reload or a deletion installed a map, with <see cref="MindmapReloaded"/>.
    /// </summary>
    event EventHandler<MindmapChangedEventArgs>? Changed;
}
