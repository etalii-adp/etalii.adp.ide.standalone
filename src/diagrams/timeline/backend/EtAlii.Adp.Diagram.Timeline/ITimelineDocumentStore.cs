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
    /// Writes <paramref name="entry"/> back and tells every session on it. Refuses while the
    /// document does not parse, so a file that is already broken is never made worse
    /// (Requirement 2.4).
    /// </summary>
    /// <remarks>
    /// <b>The entry is a parameter rather than something this looked up, and that is the fix for a
    /// data-loss defect.</b> It used to call <see cref="GetOrLoad"/> itself, so it wrote whatever
    /// was in the cache at save time - and <see cref="Reload"/> evicts. A reload landing between a
    /// command's edit and its save therefore discarded the edit and REPORTED SUCCESS, which put the
    /// command's inverse on the undo stack for a change that never happened. Passing the entry makes
    /// that ordering unrepresentable: what the caller edited is what gets written.
    /// </remarks>
    /// <returns>Empty on success, or why it was not written.</returns>
    string Save(string path, TimelineDocumentEntry entry);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// Re-reads a document something outside changed, and tells the sessions on it. A no-op
    /// while the store's own save of that path is in flight, and afterwards while the file still
    /// holds what that save wrote: its own write on disk is not an external change, and must not
    /// bounce back as one however late the watcher reports it.
    /// </summary>
    void Reload(string path);

    /// <summary>Raised after a save, and after an external change is picked up.</summary>
    event EventHandler<TimelineDocumentChangedEventArgs>? Changed;
}
