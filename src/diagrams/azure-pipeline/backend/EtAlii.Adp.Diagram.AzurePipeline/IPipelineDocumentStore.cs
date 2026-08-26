namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// The one owner of pipeline documents on disk.
/// </summary>
/// <remarks>
/// <para>
/// One document per open pipeline, keyed by path, so two diagrams on the same file share an
/// instance of it and an edit made through one is visible in the other without a reload.
/// </para>
/// <para>
/// Every call carries the workspace root as well as the path, which the C4 store has no need of.
/// A pipeline is not necessarily one file: its templates are resolved relative to the root and may
/// only be read from inside it, so the root is part of the question "what does this file say?" and
/// not an ambient setting.
/// </para>
/// </remarks>
public interface IPipelineDocumentStore
{
    /// <summary>
    /// The pipeline at <paramref name="path"/>, loaded once and kept. A file that is not there yet
    /// yields an empty document rather than throwing, so a diagram whose body has not been written
    /// still opens and the first save creates it.
    /// </summary>
    PipelineDocumentEntry GetOrLoad(string rootPath, string path);

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document does not
    /// parse, so a file that is already broken is never made worse.
    /// </summary>
    /// <returns>Empty on success, or why it was not written.</returns>
    string Save(string rootPath, string path);

    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document - for a
    /// change to how it is drawn rather than to what it says.
    /// </summary>
    void Touch(string rootPath, string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// Re-reads a document something outside changed, and tells the sessions on it. Templates are
    /// forgotten too: the edit may well have been to one of them.
    /// </summary>
    void Reload(string rootPath, string path);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<PipelineDocumentChangedEventArgs>? Changed;
}
