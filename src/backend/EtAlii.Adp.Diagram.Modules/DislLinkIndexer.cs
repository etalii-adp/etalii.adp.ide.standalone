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
/// <param name="renew">The bytes of a file at a path with a new document id (<see cref="DislOpening.Renewed"/>); without it, no document of the type is given one.</param>
/// <param name="origin">The type's origin (<c>generic/timeline</c>), which a lookup's <c>origins</c> narrow to.</param>
/// <param name="creation">The kinds of element another tool can create in a file of the type, and the bytes of a file with one more (spec 092, FR-025); without it, none.</param>
public sealed class DislLinkIndexer(
    IReadOnlyCollection<string> extensions,
    Func<byte[], string, (DislDiagram Diagram, string? DocumentId)> read,
    Func<byte[], string, byte[]?>? renew = null,
    string? origin = null,
    DislElementCreation? creation = null) : ILinkIndexer
{
    /// <inheritdoc />
    public string? Origin => origin;

    /// <inheritdoc />
    public bool Claims(string path) => extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IndexedDocument Index(string path, byte[] bytes)
    {
        (DislDiagram diagram, string? documentId) = read(bytes, path);
        return DislLinks.Indexed(diagram, path, documentId);
    }

    /// <inheritdoc />
    public byte[]? Renewed(string path, byte[] bytes) => renew?.Invoke(bytes, path);

    /// <inheritdoc />
    public IReadOnlyList<LinkElementKind> CreatableKinds => creation?.Kinds ?? [];

    /// <inheritdoc />
    public string? NewElementId() => creation?.NewId();

    /// <inheritdoc />
    public byte[]? WithElement(string path, byte[] bytes, string kind, string id, string name) =>
        creation is not null && creation.Kinds.Any(offered => offered.Id == kind) ? creation.Create(bytes, kind, id, name) : null;
}

/// <summary>What a diagram type lets another tool create in one of its files (spec 092, FR-025): the kinds, and how one is added.</summary>
/// <param name="Kinds">The kinds of element, in the order a menu offers them.</param>
/// <param name="NewId">A new element id, as the type's definition makes one.</param>
/// <param name="Create">The bytes of a file with one more element of a kind, its id and its name given; null when it cannot be added.</param>
public sealed record DislElementCreation(IReadOnlyList<LinkElementKind> Kinds, Func<string> NewId, Func<byte[], string, string, string, byte[]?> Create);
