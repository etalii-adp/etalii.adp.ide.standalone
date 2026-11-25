namespace EtAlii.Adp.Specification.Fbl.Links;

/// <summary>
/// One document of a workspace as its index keeps it (FBL 17.3): its path, with forward slashes, its document id, null for a
/// body that holds none yet, which nothing resolves to by id until it is given one, the attribute its tool type names an
/// element by (DISL's and DESL's <c>linkable</c>), null to use an element's first attribute, its elements that can be linked
/// to in document order, two with one id both kept as a body may hold them, and the links it holds.
/// </summary>
public sealed record IndexedDocument(string Path, string? DocumentId, string? NameProperty, IReadOnlyList<IndexedElement> Elements, IReadOnlyList<HeldLink> Links)
{
    /// <summary>The file name of <see cref="Path"/>, with its extension and without its folder: the document name of a link.</summary>
    public string FileName => FileNameOf(Path);

    /// <summary>The file name of <paramref name="path"/>, with its extension and without its folder.</summary>
    public static string FileNameOf(string path) => path[(path.LastIndexOf('/') + 1)..];
}

/// <summary>An element of a document that can be linked to: its id, and its attributes of a text type in declaration order.</summary>
public sealed record IndexedElement(string Id, IReadOnlyList<KeyValuePair<string, string>> Attributes)
{
    /// <summary>The value of the attribute <paramref name="name"/>, or null where the element has none.</summary>
    public string? ValueOf(string name)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute.Key == name) return attribute.Value;
        }
        return null;
    }
}

/// <summary>A link a document holds, and the element or value of it that holds it, as its tool type names that.</summary>
public sealed record HeldLink(string Holder, string Text);
