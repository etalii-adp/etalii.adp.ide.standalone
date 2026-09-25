namespace EtAlii.Adp.Documents;

/// <summary>
/// The one thing a shared edit needs from a module's document store: to re-read a document that
/// changed on disk and tell every session showing it (backend-centralization R6).
/// </summary>
/// <remarks>
/// A module's store interface extends this rather than declaring its own <c>Reload</c>, so
/// <c>RestoreDocumentCommand&lt;TStore&gt;</c> can restore that module's files without the module
/// copying the command.
/// </remarks>
public interface IReloadableDocumentStore
{
    /// <summary>
    /// Re-reads the document at <paramref name="path"/> and tells the sessions on it. A store may
    /// ignore this while its own save of that path is in flight: its own write on disk is not an
    /// external change, and must not bounce back as one.
    /// </summary>
    void Reload(string path);
}
