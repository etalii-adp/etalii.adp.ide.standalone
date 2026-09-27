using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The one owner of RDF documents on disk: one per open file, keyed by path, so every reading of
/// the family - the data graph and the sibling readings joining it - shares a file's document and
/// an edit made through one is visible in the others.
/// </summary>
/// <remarks>
/// This is the triplestore document store the family's specifications name: a file is parsed once
/// into triples plus a line-level concrete syntax, and siblings add projections over the model,
/// never parsers of their own. Turtle and N-Triples come through the same door - N-Triples is a
/// subset - and everything else is refused upstream at the definition, not here.
/// <para>
/// <see cref="IReloadableDocumentStore.Reload"/> is this store's too, declared on the shared
/// interface so the shared restore edit (backend-centralization R6) can reload this module's
/// documents: it re-reads a document something outside changed and tells the sessions on it. It
/// is a no-op while the store's own save of that path is in flight: its own write on disk is not
/// an external change, and must not bounce back as one. A reload that cannot read keeps the last
/// good document and tells nobody (R2.4).
/// </para>
/// </remarks>
public interface IRdfDocumentStore : IReloadableDocumentStore
{
    /// <summary>
    /// The document at <paramref name="path"/>, loaded once and kept. A file that is not there
    /// yet yields an empty document rather than throwing, so a freshly created diagram opens and
    /// the first save creates it.
    /// </summary>
    RdfDocumentEntry GetOrLoad(string path);

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document does
    /// not parse, so a file that is already broken is never made worse (Requirement 1.5).
    /// </summary>
    /// <returns>Empty on success, or why it was not written.</returns>
    /// <remarks>
    /// <b>The entry is a parameter rather than something this looked up, and that is the fix for a
    /// data-loss defect.</b> It used to call <see cref="GetOrLoad"/> itself, so it wrote whatever was
    /// in the cache at save time - and a <see cref="IReloadableDocumentStore.Reload"/> replaced that entry. A reload landing between a
    /// command's edit and its save therefore discarded the edit and REPORTED SUCCESS, which put the
    /// command's inverse on the undo stack for a change that never happened. Passing the entry makes
    /// that ordering unrepresentable: what the caller edited is what gets written.
    /// </remarks>
    string Save(string path, RdfDocumentEntry entry);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// The watcher saw the body deleted: the document becomes what a first open of a missing body
    /// shows, and the sessions are told (R2.5). The one call that clears a document a reload kept.
    /// </summary>
    void BodyDeleted(string path);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<RdfDocumentChangedEventArgs>? Changed;
}
