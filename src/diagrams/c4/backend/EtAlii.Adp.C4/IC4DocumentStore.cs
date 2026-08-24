namespace EtAlii.Adp.C4;

/// <summary>What changed about a C4 document, for the sessions that show it.</summary>
public sealed record C4DocumentChangedEventArgs(string Path, C4Workspace Workspace);

/// <summary>
/// The one owner of C4 documents on disk. Several diagrams may open one document - that is what
/// "model once, view many" means - so they must share one instance of it, or an edit made
/// through one view would be invisible to another until a reload (c4-diagrams Requirement 1.2).
/// </summary>
public interface IC4DocumentStore
{
    /// <summary>
    /// The document at <paramref name="path"/>, loading it once and keeping it. A missing file
    /// yields an empty document rather than throwing: a diagram whose body has not been written
    /// yet still opens, and the first save creates it.
    /// </summary>
    C4Document GetOrLoad(string path);

    /// <summary>The parsed workspace for <paramref name="path"/>, reusing the loaded document.</summary>
    C4Workspace WorkspaceOf(string path);

    /// <summary>
    /// Writes the document back and tells every session on it. Refuses while the document on
    /// disk is unparseable, so a broken file is never made worse (Requirement 3.4).
    /// </summary>
    void Save(string path);

    /// <summary>Forgets a document, so the next open reads it afresh.</summary>
    void Forget(string path);

    /// <summary>Raised after a save, and after an external edit is picked up.</summary>
    event EventHandler<C4DocumentChangedEventArgs>? Changed;
}
