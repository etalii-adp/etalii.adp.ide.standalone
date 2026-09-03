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
public interface IDatabricksDocumentStore
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
    string Save(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// Re-reads a document something outside changed, and tells the sessions on it. A no-op
    /// while the store's own save of that path is in flight: its own write on disk is not an
    /// external change, and must not bounce back as one.
    /// </summary>
    void Reload(string path);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<DatabricksDocumentChangedEventArgs>? Changed;
}
