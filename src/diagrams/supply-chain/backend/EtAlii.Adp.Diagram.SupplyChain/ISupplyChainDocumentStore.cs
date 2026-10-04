using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The loaded <c>.supply</c> documents, one per body path and shared by every session viewing it.
/// </summary>
/// <remarks>
/// <b>An <see cref="IReloadableDocumentStore"/></b>, so the shared restore command can put a
/// document's text back and have this store re-read it - which is every command's undo.
/// </remarks>
public interface ISupplyChainDocumentStore : IReloadableDocumentStore
{
    /// <summary>The document whose body is at <paramref name="path"/>, loaded once and kept.</summary>
    SupplyChainDocumentEntry GetOrLoad(string path);

    /// <summary>Writes <paramref name="document"/>, the one the caller edited, to <paramref name="path"/>.</summary>
    /// <remarks><b>A document that could not be read is refused, never written.</b></remarks>
    DocumentSaveResult Save(string path, LineDocument document);

    /// <summary>Drops a loaded document, so the next open reads the file afresh.</summary>
    void Forget(string path);

    /// <summary>The body was deleted: the document ends as a new, empty one.</summary>
    void BodyDeleted(string path);

    /// <summary>Raised when the document at a path has been replaced, for the sessions on it.</summary>
    event EventHandler<SupplyChainDocumentChangedEventArgs>? Changed;
}

/// <summary>The document at <paramref name="path"/> was replaced.</summary>
public sealed class SupplyChainDocumentChangedEventArgs(string path) : EventArgs
{
    /// <summary>The body path whose document changed.</summary>
    public string Path { get; } = path;
}
