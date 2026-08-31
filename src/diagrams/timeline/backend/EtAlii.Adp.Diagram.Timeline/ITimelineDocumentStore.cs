namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The one owner of timeline documents on disk: one per open timeline, keyed by path, so two
/// diagrams on the same file share it and an edit made through one is visible in the other.
/// </summary>
/// <remarks>
/// No workspace root in any signature, deliberately. The pipeline store carries one because a
/// pipeline's templates resolve relative to it; a timeline is one file and reads nothing else,
/// so a root would be a parameter every caller supplies and nothing uses.
/// </remarks>
public interface ITimelineDocumentStore
{
    /// <summary>
    /// The timeline at <paramref name="path"/>, loaded once and kept. A file that is not there
    /// yet yields an empty document rather than throwing, so a freshly created diagram opens and
    /// the first save creates it.
    /// </summary>
    TimelineDocumentEntry GetOrLoad(string path);

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document does
    /// not parse, so a file that is already broken is never made worse (Requirement 2.4).
    /// </summary>
    /// <returns>Empty on success, or why it was not written.</returns>
    string Save(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>Re-reads a document something outside changed, and tells the sessions on it.</summary>
    void Reload(string path);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<TimelineDocumentChangedEventArgs>? Changed;
}
