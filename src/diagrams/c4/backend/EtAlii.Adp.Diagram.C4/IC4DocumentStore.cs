using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The one owner of C4 documents on disk. Several diagrams may open one document - that is what
/// "model once, view many" means - so they must share one instance of it, or an edit made
/// through one view would be invisible to another until a reload (c4-diagrams Requirement 1.2).
/// </summary>
/// <remarks>
/// Everything a store does is declared by <see cref="IDiagramDocumentStore{TBody, TModel}"/>; this
/// names the C4 family's, and adds the one thing that is its own.
/// </remarks>
public interface IC4DocumentStore : IDiagramDocumentStore<C4Document, C4Workspace>
{
    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document.
    /// A drag changes where an element is drawn but not what the model says, so it has a
    /// sidecar to write and nothing to save (Requirement 8.3).
    /// </summary>
    void Touch(string path);
}
