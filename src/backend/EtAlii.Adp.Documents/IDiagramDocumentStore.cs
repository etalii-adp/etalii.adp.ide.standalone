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
