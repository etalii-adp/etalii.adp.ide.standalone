namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Where the commands reach a mindmap by its body path. One loaded document per open
/// diagram, so two commands on the same map - or two connections viewing it - share one tree
/// and one history of changes (Requirement 2.4).
/// </summary>
public interface IMindmapDocumentStore
{
    /// <summary>
    /// The document at <paramref name="bodyPath"/>, loading it on first ask. A missing body
    /// opens as an empty map named after the file (Requirement 2.5).
    /// </summary>
    /// <exception cref="MindmapFormatException">The file exists but is not a Freeplane map.</exception>
    MindmapDocument GetOrLoad(string bodyPath);

    /// <summary>The loaded document, or null when nothing has opened it; never loads.</summary>
    MindmapDocument? Get(string bodyPath);

    /// <summary>Writes the loaded document back to <paramref name="bodyPath"/>, atomically, and reports the change.</summary>
    void Save(string bodyPath, MindmapChange change);

    /// <summary>Raised after every save, with what changed, so the open connections can be told.</summary>
    event EventHandler<MindmapChangedEventArgs>? Changed;
}
