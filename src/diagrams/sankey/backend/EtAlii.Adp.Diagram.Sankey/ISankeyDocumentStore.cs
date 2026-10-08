using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The loaded <c>.skv</c> documents, one per body path and shared by every session viewing it.
/// </summary>
/// <remarks>
/// <b>An <see cref="IReloadableDocumentStore"/></b>, so the shared restore command can put a
/// document's text back and have this store re-read it - which is every command's undo.
/// </remarks>
public interface ISankeyDocumentStore : IReloadableDocumentStore
{
    /// <summary>The document whose body is at <paramref name="path"/>, loaded once and kept.</summary>
    SankeyDocumentEntry GetOrLoad(string path);

    /// <summary>Writes <paramref name="document"/>, the one the caller edited, to <paramref name="path"/>.</summary>
    /// <remarks><b>A document that could not be read is refused, never written.</b></remarks>
    DocumentSaveResult Save(string path, LineDocument document);

    /// <summary>The body was deleted: the document ends as a new, empty one.</summary>
    void BodyDeleted(string path);

    /// <summary>Raised when the document at a path has been replaced, for the sessions on it.</summary>
    event EventHandler<SankeyDocumentChangedEventArgs>? Changed;
}

/// <summary>The document at <paramref name="path"/> was replaced.</summary>
public sealed class SankeyDocumentChangedEventArgs(string path) : EventArgs
{
    /// <summary>The body path whose document changed.</summary>
    public string Path { get; } = path;
}
