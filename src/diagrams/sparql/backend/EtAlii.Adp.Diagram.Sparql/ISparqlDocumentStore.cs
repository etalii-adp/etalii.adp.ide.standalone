namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The one owner of loaded query files, keyed by path. Its public surface ends at reading,
/// deliberately: there is no save member of any kind, because nothing in this module writes a
/// <c>.rq</c> - the byte-identical round trip is held by construction, and the no-writer surface
/// test pins that this stays true.
/// </summary>
public interface ISparqlDocumentStore
{
    /// <summary>
    /// The document at <paramref name="path"/>, loaded once and kept. A file that is not there
    /// yields an entry whose error says so, because this module never creates query files either.
    /// </summary>
    SparqlDocumentEntry GetOrLoad(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>Re-reads a document something outside changed, and tells the sessions on it.</summary>
    void Reload(string path);

    /// <summary>Raised after an external change is picked up - the only way an entry ever changes.</summary>
    event EventHandler<SparqlDocumentChangedEventArgs>? Changed;
}
