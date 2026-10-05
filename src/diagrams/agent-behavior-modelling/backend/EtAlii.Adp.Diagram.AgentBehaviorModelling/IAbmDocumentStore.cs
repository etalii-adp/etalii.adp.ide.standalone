using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The loaded Markdown documents, one per body path and shared by every session viewing it.
/// </summary>
/// <remarks>
/// <b>An <see cref="IReloadableDocumentStore"/></b>, so the shared restore command (backend-centralization
/// R6) can put a document's text back and have this store re-read it - which is every command's undo.
/// </remarks>
public interface IAbmDocumentStore : IReloadableDocumentStore
{
    /// <summary>The document whose body is at <paramref name="path"/>, loaded once and kept.</summary>
    AbmDocumentEntry GetOrLoad(string path);

    /// <summary>Writes <paramref name="document"/>, the one the caller edited, to <paramref name="path"/>.</summary>
    /// <returns>
    /// <see cref="DocumentSaveResult.Ok"/>, a warning, or a failure whose sentence a user can act on.
    /// A caller surfaces a failure; the build fails on one that is dropped.
    /// </returns>
    /// <remarks>
    /// <b>A document that could not be read is refused, never written.</b> See
    /// <see cref="AbmDocumentEntry.Unreadable"/>: its emptiness is not the document, and writing it would
    /// replace the only copy on disk.
    /// </remarks>
    DocumentSaveResult Save(string path, AbmBody document);

    /// <summary>Drops a loaded document, so the next open reads the file afresh.</summary>
    void Forget(string path);

    /// <summary>
    /// The body was deleted: the watcher's evidence, not a read that failed. So the document ends
    /// as a new, empty one, rather than the last one kept alive.
    /// </summary>
    void BodyDeleted(string path);

    /// <summary>Raised when the document at a path has been replaced, for the sessions on it.</summary>
    event EventHandler<AbmDocumentChangedEventArgs>? Changed;
}

/// <summary>The document at <paramref name="path"/> was replaced.</summary>
public sealed class AbmDocumentChangedEventArgs(string path) : EventArgs
{
    /// <summary>The body path whose document changed.</summary>
    public string Path { get; } = path;
}
