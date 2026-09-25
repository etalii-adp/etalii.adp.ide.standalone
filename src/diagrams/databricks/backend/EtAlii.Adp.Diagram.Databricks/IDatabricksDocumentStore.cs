using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The one owner of Databricks configuration documents on disk: one per open file, keyed by
/// path, so the family's three diagram types share a file's document and an edit made through
/// one is visible in the others.
/// </summary>
/// <remarks>
/// One store for all three formats, deliberately: <c>databricks.yml</c>, job resource YAML and
/// pipeline settings JSON are one configuration family, a single file legitimately serves more
/// than one diagram (a bundle declaring a job inline is both), and JSON reads through the same
/// YAML door - so three stores would be three copies of the same lifecycle keyed by the same
/// paths.
/// </remarks>
public interface IDatabricksDocumentStore : IReloadableDocumentStore
{
    /// <summary>
    /// The document at <paramref name="path"/>, loaded once and kept. A file that is not there
    /// yet yields an empty document rather than throwing, so a freshly created diagram opens and
    /// the first save creates it.
    /// </summary>
    DatabricksDocumentEntry GetOrLoad(string path);

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document does
    /// not parse, so a file that is already broken is never made worse (Requirement 2.3).
    /// </summary>
    /// <returns>Empty on success, or why it was not written.</returns>
    /// <remarks>
    /// <b>The entry is a parameter rather than something this looked up, and that is the fix for a
    /// data-loss defect.</b> It used to call <see cref="GetOrLoad"/> itself, so it wrote whatever was
    /// in the cache at save time - and <see cref="IReloadableDocumentStore.Reload"/> evicts. A reload landing between a
    /// command's edit and its save therefore discarded the edit and REPORTED SUCCESS, which put the
    /// command's inverse on the undo stack for a change that never happened. Passing the entry makes
    /// that ordering unrepresentable: what the caller edited is what gets written.
    /// </remarks>
    string Save(string path, DatabricksDocumentEntry entry);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<DatabricksDocumentChangedEventArgs>? Changed;
}
