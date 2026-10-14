namespace EtAlii.Adp.Documents;

/// <summary>
/// What every diagram module's document store answers to from outside the module: a body changed
/// on disk, a body deleted, and a document to drop (code-structure FR-028).
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared once here, so a module's own interface declares none of it.</b> Before, each module
/// re-declared <c>BodyDeleted</c> and <c>Forget</c> in its own store interface, and wrote a reloader
/// class that only forwarded to them. A module's interface now extends this one and adds only what
/// is its own; where it adds nothing it stays as a marker, the distinct type its registrations
/// resolve.
/// </para>
/// <para>
/// <b><see cref="BodyDeleted"/> is not a <see cref="IReloadableDocumentStore.Reload"/>.</b> A store
/// keeps its last good document when a reload cannot read the body, because a publish in flight
/// leaves the body missing for an instant. A deleted body must instead end the document, or the
/// deleted diagram stays on the canvas.
/// </para>
/// </remarks>
public interface IDiagramDocumentStore : IReloadableDocumentStore
{
    /// <summary>The body at <paramref name="path"/> was deleted: the document ends as a new, empty one, and the sessions on it are told.</summary>
    void BodyDeleted(string path);

    /// <summary>Drops the loaded document at <paramref name="path"/>, so the next open reads the file afresh.</summary>
    void Forget(string path);
}

/// <summary>
/// A diagram module's document store as its sessions and resolvers read it: one document per body
/// path, loaded once and shared, and a change event.
/// </summary>
/// <remarks>
/// <b>The reading half on its own, for a module that never writes its files</b>, such as a SPARQL
/// query, which is edited in a text editor and only drawn. Such a store implements this and has no
/// save to refuse with, so a session or resolver on it cannot write by mistake.
/// </remarks>
/// <typeparam name="TBody">The type a body is read into, such as <see cref="LineDocument"/>.</typeparam>
/// <typeparam name="TModel">What the module's parser reads from a body.</typeparam>
public interface IReadableDiagramDocumentStore<TBody, TModel> : IDiagramDocumentStore
{
    /// <summary>The document whose body is at <paramref name="path"/>, loaded once and kept.</summary>
    DocumentEntry<TBody, TModel> GetOrLoad(string path);

    /// <summary>Raised when the document at a path has been replaced, for the sessions on it.</summary>
    event EventHandler<DocumentChangedEventArgs>? Changed;
}

/// <summary>
/// A diagram module's document store as its sessions, resolvers and commands use it: the reading
/// half, and a save of the document as the caller edited it.
/// </summary>
/// <remarks>
/// <see cref="LifecycleDocumentStore{TBody, TModel}"/> implements it, so a module's own store
/// interface extends this and declares nothing; it stays as the distinct type the module's
/// registrations and its restore command resolve.
/// </remarks>
/// <typeparam name="TBody">The type a body is read into and edited as, such as <see cref="LineDocument"/>.</typeparam>
/// <typeparam name="TModel">What the module's parser reads from a body.</typeparam>
public interface IDiagramDocumentStore<TBody, TModel> : IReadableDiagramDocumentStore<TBody, TModel>
{
    /// <summary>Writes <paramref name="document"/>, the one the caller edited, to <paramref name="path"/>.</summary>
    /// <remarks><b>A document that could not be read is refused, never written.</b></remarks>
    DocumentSaveResult Save(string path, TBody document);
}
