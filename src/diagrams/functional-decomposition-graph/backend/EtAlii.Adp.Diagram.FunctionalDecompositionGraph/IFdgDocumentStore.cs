namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The loaded <c>.fdg</c> documents, one per body path and shared by every session viewing it.
/// </summary>
/// <remarks>
/// <b>There is no save here yet, on purpose.</b> Saving arrives with task 12's commands, which are the
/// only things that write a document, and which have to surface a failed save rather than drop it. A
/// save written now would have no caller and would decide what a failure does without the code
/// that must answer it.
/// </remarks>
public interface IFdgDocumentStore
{
    /// <summary>The document whose body is at <paramref name="path"/>, loaded once and kept.</summary>
    FdgDocumentEntry GetOrLoad(string path);

    /// <summary>Drops a loaded document, so the next open reads the file afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// Re-reads a body changed on disk. A read that fails keeps the last good document, and the
    /// sessions hear nothing.
    /// </summary>
    void Reload(string path);

    /// <summary>
    /// The body was deleted: the watcher's evidence, not a read that failed. So the document ends
    /// as a new, empty one, rather than the last one kept alive.
    /// </summary>
    void BodyDeleted(string path);

    /// <summary>Raised when the document at a path has been replaced, for the sessions on it.</summary>
    event EventHandler<FdgDocumentChangedEventArgs>? Changed;
}

/// <summary>The document at <paramref name="Path"/> was replaced.</summary>
public sealed class FdgDocumentChangedEventArgs(string path) : EventArgs
{
    /// <summary>The body path whose document changed.</summary>
    public string Path { get; } = path;
}
