using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The one owner of `.owm` documents on disk. Several connections may have one map open, so
/// they must share one instance of it, or an edit made through one would be invisible to
/// another until a reload (Requirement 10.6).
/// </summary>
/// <remarks>
/// Everything a store does is declared by <see cref="IDiagramDocumentStore{TBody, TModel}"/>; this
/// names the Wardley map's, and adds the identities held beside each document. A missing file
/// opens as an empty map (Requirement 2.4), and the sibling is created on the first save.
/// </remarks>
public interface IWardleyDocumentStore : IDiagramDocumentStore<LineDocument, WardleyMap>
{
    /// <summary>
    /// The identities of the map at <paramref name="path"/>, reconciled once and held for as
    /// long as the document is (Requirement 4.3).
    /// </summary>
    /// <remarks>
    /// This has to live here rather than being reconciled by each caller. An unmatched element
    /// is given a <b>fresh</b> <c>ShortGuid</c>, so two callers reconciling the same map
    /// against the same absent sidecar would mint two different sets and agree on nothing - a
    /// session would hand out one id and a command handler would look for another. Holding them
    /// beside the document is also what lets ids be assigned on load and written only on the
    /// first real save, rather than on open. A save whose identity sidecar could not be written
    /// carries that as its <see cref="DocumentSaveResult.Warning"/>.
    /// </remarks>
    IReadOnlyList<WardleyIdentityEntry> Identities(string path);

    /// <summary>
    /// Moves an identity from one key to another, for a rename (Requirement 4.4).
    /// </summary>
    /// <remarks>
    /// Reconciliation matches by key, and a component's key is its name - so on its own a rename
    /// looks exactly like one element disappearing and another arriving. The identity has to be
    /// carried across in the same command that rewrites the statements, or the selection, the
    /// pushed element ids and every undo entry naming the element all break on a rename, which
    /// is the "small catastrophe" Requirement 4 exists to prevent.
    /// </remarks>
    void Rekey(string path, string kind, string oldKey, string newKey);
}
