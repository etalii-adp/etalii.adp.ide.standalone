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

    /// <summary>The tool type's origin (<c>generic/timeline</c>), stamped on every document it indexes; null when it names none.</summary>
    string? Origin { get; }

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

    /// <summary>
    /// The kinds of element a document of this tool type can be given from another tool, by name alone and without a position:
    /// what a knowledge relation's "Create" offers when its target is such a document (spec 092, FR-025). None by default.
    /// </summary>
    IReadOnlyList<LinkElementKind> CreatableKinds => [];

    /// <summary>A new element id, made as the tool type's definition makes one, for an element another tool creates; null when it creates none.</summary>
    string? NewElementId() => null;

    /// <summary>
    /// The body <paramref name="bytes"/> of the document at <paramref name="path"/> with a new element of <paramref name="kind"/>,
    /// whose id is <paramref name="id"/> and whose name is <paramref name="name"/>, placed where the tool type places an element
    /// added without a position (spec 092, FR-025); null when it cannot be given one.
    /// </summary>
    byte[]? WithElement(string path, byte[] bytes, string kind, string id, string name) => null;
}

/// <summary>A kind of element another tool can create in a document: its element type, and what a person reads it as.</summary>
/// <param name="Id">The element type, as the tool type's definition names it (<c>Moment</c>).</param>
/// <param name="Label">What a menu says of it (<c>moment</c>).</param>
public sealed record LinkElementKind(string Id, string Label);
