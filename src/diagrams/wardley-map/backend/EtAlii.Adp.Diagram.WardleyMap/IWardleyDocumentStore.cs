namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The one owner of `.owm` documents on disk. Several connections may have one map open, so
/// they must share one instance of it, or an edit made through one would be invisible to
/// another until a reload (Requirement 10.6).
/// </summary>
public interface IWardleyDocumentStore
{
    /// <summary>
    /// The document at <paramref name="path"/>, loading it once and keeping it. A missing file
    /// yields an empty document rather than throwing: Requirement 2.4 opens a map whose `.owm`
    /// sibling is absent as an empty map, and creates the sibling on the first save.
    /// </summary>
    WardleyDocument GetOrLoad(string path);

    /// <summary>
    /// The identities of the map at <paramref name="path"/>, reconciled once and held for as
    /// long as the document is (Requirement 4.3).
    /// </summary>
    /// <remarks>
    /// This has to live here rather than being reconciled by each caller. An unmatched element
    /// is given a <b>fresh</b> <c>ShortGuid</c>, so two callers reconciling the same map
    /// against the same absent sidecar would mint two different sets and agree on nothing - a
    /// session would hand out one id and a command handler would look for another. Holding them
    /// beside the document is also what lets ids be assigned on load and written only on the
    /// first real save, rather than on open.
    /// </remarks>
    IReadOnlyList<WardleyIdentityEntry> Identities(string path);

    /// <summary>
    /// Writes the document back atomically and tells every session on it (Requirement 3.6).
    /// </summary>
    void Save(string path);

    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document.
    /// </summary>
    void Touch(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// Re-reads a document an external tool changed, and tells the sessions on it. The reload
    /// is never recorded on the history: it arrives through the watcher and never becomes a
    /// command (Requirement 10.7).
    /// </summary>
    void Reload(string path);

    /// <summary>Raised after a save, and after an external edit is picked up.</summary>
    event EventHandler<WardleyDocumentChangedEventArgs>? Changed;
}
