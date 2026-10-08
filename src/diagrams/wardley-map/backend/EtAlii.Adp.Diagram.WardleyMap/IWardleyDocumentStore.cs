using EtAlii.Adp.Documents;

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
    /// Moves an identity from one key to another, for a rename (Requirement 4.4).
    /// </summary>
    /// <remarks>
    /// Reconciliation matches by key, and a component's key is its name - so on its own a rename
    /// looks exactly like one element disappearing and another arriving. The identity has to be
    /// carried across in the same command that rewrites the statements, or the selection, the
    /// pushed element ids and every undo entry naming the element all break on a rename, which
    /// is the "small catastrophe" Requirement 4 exists to prevent.
    /// </remarks>
    void Rekey(string path, string kind, string oldKey, string newKey);

    /// <summary>
    /// Writes the document back atomically and tells every session on it (Requirement 3.6).
    /// </summary>
    /// <returns>
    /// A result whose <see cref="DocumentSaveResult.Error"/> is empty when the document reached the
    /// disk and a sentence the user can read when it did not, and whose
    /// <see cref="DocumentSaveResult.Warning"/> carries an identity sidecar that could not be written
    /// beside a document that was. **A caller must surface it rather than drop it.** The edit
    /// survives in memory either way, so a caller that ignores this answers the user with a save
    /// that never happened - which is what this returned nothing at all in order to do. The shape was
    /// this module's own <c>WardleyPublishResult</c> until backend-centralization R3.1 shared it.
    /// </returns>
    /// <remarks>
    /// <b>The document is a parameter rather than something this looked up, and that is the fix for
    /// a data-loss defect.</b> It used to call <see cref="GetOrLoad"/> itself, so it wrote whatever
    /// was in the cache at save time - and <see cref="Reload"/> replaces it. A reload landing between a
    /// command's edit and its save therefore discarded the edit and REPORTED SUCCESS, which put the
    /// command's inverse on the undo stack for a change that never happened. Passing the document
    /// makes that ordering unrepresentable: what the caller edited is what gets written.
    /// </remarks>
    DocumentSaveResult Save(string path, WardleyDocument document);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// Re-reads a document an external tool changed, and tells the sessions on it. The reload
    /// is never recorded on the history: it arrives through the watcher and never becomes a
    /// command (Requirement 10.7). A no-op while the store's own save of that path is in
    /// flight: its own write on disk is not an external change, and must not bounce back as
    /// one. A reload that cannot read keeps the last good document and tells nobody (R2.4).
    /// </summary>
    void Reload(string path);

    /// <summary>
    /// The watcher saw the body deleted: the document becomes what a first open of a missing body
    /// shows, and the sessions are told (R2.5). The one call that clears a document a reload kept.
    /// </summary>
    void BodyDeleted(string path);

    /// <summary>Raised after a save, and after an external edit is picked up.</summary>
    event EventHandler<WardleyDocumentChangedEventArgs>? Changed;
}
