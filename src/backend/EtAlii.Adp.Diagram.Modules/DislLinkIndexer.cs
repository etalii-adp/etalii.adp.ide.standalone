using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The index's reader for a diagram type specified in DISL (FBL 17.3, DISL 0.12 §4.12): a file of one of its extensions read
/// as its module reads it, and held by its linkable elements and the links they hold. What the type is, is all in its
/// arguments; a module registers one of these and writes no indexing of its own.
/// </summary>
/// <param name="extensions">The extensions of the type's files, with their dot (<c>.tml</c>), compared without regard to case.</param>
/// <param name="read">The diagram the bytes of a file at a path hold, built under the type's specification, and the file's document id.</param>
public sealed class DislLinkIndexer(IReadOnlyCollection<string> extensions, Func<byte[], string, (DislDiagram Diagram, string? DocumentId)> read) : ILinkIndexer
{
    /// <inheritdoc />
    public bool Claims(string path) => extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IndexedDocument Index(string path, byte[] bytes)
    {
        (DislDiagram diagram, string? documentId) = read(bytes, path);
        return DislLinks.Indexed(diagram, path, documentId);
    }
}
