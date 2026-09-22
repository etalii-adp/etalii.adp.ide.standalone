namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// The one owner of loaded <c>.cld</c> files, keyed by path, so two connections on one document
/// share its parse rather than each holding their own.
/// </summary>
public interface ICausalLoopDocumentStore
{
    /// <summary>The document at <paramref name="path"/>, loaded once and kept.</summary>
    CausalLoopDocumentEntry GetOrLoad(string path);

    /// <summary>
    /// Writes the loaded document back and re-parses it. The entry's own
    /// <see cref="CausalLoopDocumentEntry.Document"/> is the thing saved, so an edit is a splice
    /// into the lines that were read rather than a reserialization of the model.
    /// </summary>
    /// <returns>An error to report, or empty when it was written.</returns>
    string Save(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>Re-reads a document something outside changed, and tells the sessions on it.</summary>
    void Reload(string path);

    /// <summary>
    /// The body was deleted - the watcher's evidence, not a read that failed - so the diagram
    /// stops being drawn. A reload that cannot read keeps the last good document; this does not.
    /// </summary>
    void BodyDeleted(string path);

    /// <summary>Raised after a change is picked up.</summary>
    event EventHandler<CausalLoopDocumentChangedEventArgs>? Changed;
}
