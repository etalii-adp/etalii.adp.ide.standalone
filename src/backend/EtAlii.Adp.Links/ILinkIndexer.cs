using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Links;

/// <summary>
/// What one tool type gives the workspace's index (FBL 17.3): which files are its documents, and a document as the index holds
/// it. A tool type whose persistence keeps a document id registers one; the index names no tool type.
/// </summary>
public interface ILinkIndexer
{
    /// <summary>Whether the file at <paramref name="path"/> is a document of this tool type, by its name alone.</summary>
    bool Claims(string path);

    /// <summary>
    /// The document at <paramref name="path"/> whose body is <paramref name="bytes"/>, as the index holds it; null when the body
    /// cannot be read, which the index then holds nothing of.
    /// </summary>
    IndexedDocument? Index(string path, byte[] bytes);

    /// <summary>
    /// The body <paramref name="bytes"/> of the document at <paramref name="path"/> with a new document id, as the fix of
    /// <c>link.ambiguous-document</c> gives every candidate but the chosen one (FBL 17.6); null when it cannot be given one.
    /// </summary>
    byte[]? Renewed(string path, byte[] bytes);
}
