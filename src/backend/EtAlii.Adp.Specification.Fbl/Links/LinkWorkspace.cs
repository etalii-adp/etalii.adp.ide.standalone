namespace EtAlii.Adp.Specification.Fbl.Links;

/// <summary>
/// The documents of one workspace root (FBL 17.3), each as its tool type indexes it, kept in path order, and a link resolved
/// against them. Resolution is a function of the index alone: two hosts with the same workspace resolve a link to the same
/// element or report the same finding. Paths are compared as text, with forward slashes.
/// </summary>
public sealed class LinkWorkspace
{
    private readonly SortedDictionary<string, IndexedDocument> _documents = new(StringComparer.Ordinal);

    /// <summary>A workspace of the root <paramref name="root"/> holding <paramref name="documents"/>.</summary>
    public LinkWorkspace(string root, IEnumerable<IndexedDocument>? documents = null)
    {
        Root = root.TrimEnd('/');
        foreach (var document in documents ?? []) Put(document);
    }

    /// <summary>The workspace's root folder, without a closing slash.</summary>
    private string Root { get; }

    /// <summary>The documents, in path order.</summary>
    public IReadOnlyCollection<IndexedDocument> Documents => _documents.Values;

    /// <summary>The document at <paramref name="path"/>, or null.</summary>
    public IndexedDocument? Document(string path) => _documents.GetValueOrDefault(path);

    /// <summary>Puts <paramref name="document"/> at its path, in place of the one there: a document read again after it changed.</summary>
    public void Put(IndexedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documents[document.Path] = document;
    }

    /// <summary>A path as a finding names it: relative to the root.</summary>
    public string Display(string path) => path.StartsWith(Root + "/", StringComparison.Ordinal) ? path[(Root.Length + 1)..] : path;

    /// <summary>
    /// Resolves the link <paramref name="text"/> (FBL 17.3): parsed; the target document by its id, else by its file name
    /// among the documents of that id or, when none has it, among all that have an id; then the target element by its id.
    /// </summary>
    public LinkResolution Resolve(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (FblLink.Parse(text) is not { } link) return Finding(null, LinkCodes.Malformed, $"'{text}' is not a link.", []);

        var withId = _documents.Values.Where(document => document.DocumentId == link.DocumentId).ToList();
        IndexedDocument target;
        if (withId.Count == 1)
        {
            target = withId[0];
        }
        else
        {
            var pool = withId.Count == 0 ? _documents.Values.Where(document => document.DocumentId is not null) : withId;
            var named = pool.Where(document => document.FileName == link.DocumentName).ToList();
            if (named.Count == 0) return Finding(link, LinkCodes.BrokenDocument, $"No document '{link.DocumentName}' with id '{link.DocumentId}' is in the workspace.", []);
            if (named.Count > 1)
            {
                var candidates = named.Select(document => document.Path).ToList();
                return Finding(link, LinkCodes.AmbiguousDocument, $"{candidates.Count} documents are '{link.DocumentName}' with id '{link.DocumentId}': {string.Join(", ", candidates.Select(Display))}.", candidates);
            }
            target = named[0];
        }

        if (link.ElementId is not { } elementId) return new LinkResolution(link, target, null, FblLink.ToDocument(target.DocumentId ?? "", target.FileName), null);
        var holders = target.Elements.Where(element => element.Id == elementId).ToList();
        if (holders.Count == 0) return Finding(link, LinkCodes.BrokenElement, $"'{link.Name ?? elementId}' is no longer in '{target.FileName}'.", []);
        if (holders.Count > 1) return Finding(link, LinkCodes.AmbiguousDocument, $"'{target.FileName}' holds {holders.Count} elements with id '{elementId}'.", [target.Path]);

        (string? nameProperty, string name) = NameOf(target, holders[0], link.NameProperty);
        return new LinkResolution(link, target, holders[0], new FblLink(target.DocumentId ?? "", target.FileName, elementId, nameProperty, name), null);
    }

    /// <summary>
    /// The backlinks of the element <paramref name="elementId"/> of the document at <paramref name="path"/> (FBL 17.5): every
    /// link of the workspace that resolves to it, in path order and then in the order each document holds them.
    /// </summary>
    public IReadOnlyList<Backlink> Backlinks(string path, string elementId)
    {
        var backlinks = new List<Backlink>();
        var name = NameOf(path, elementId);
        foreach (var document in _documents.Values)
        {
            foreach (var held in document.Links)
            {
                var resolution = Resolve(held.Text);
                if (resolution is { Resolved: true, Link: { } link, Document: { } target } && target.Path == path && link.ElementId == elementId) backlinks.Add(new Backlink(document.Path, held.Holder) { TargetName = name });
            }
        }
        return backlinks;
    }

    /// <summary>The name of the element <paramref name="elementId"/> of the document at <paramref name="path"/>, as a link names it (FBL 17.1); null where the index holds no such element.</summary>
    public string? NameOf(string path, string elementId)
    {
        if (Document(path) is not { } document || document.Elements.FirstOrDefault(candidate => candidate.Id == elementId) is not { } element) return null;
        (string? _, string name) = NameOf(document, element, null);
        return name;
    }

    /// <summary>
    /// The documents whose findings depend on the document at <paramref name="path"/> (FBL 17.5): every other one that holds a
    /// link naming it, by its document id while the workspace holds it, or by its file name, which still names it once it is
    /// gone; in path order. A change of the document can break or mend their links, so they are judged again with it.
    /// </summary>
    public IReadOnlyList<string> Dependents(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var id = Document(path)?.DocumentId;
        var name = path[(path.LastIndexOf('/') + 1)..];
        return [.. _documents.Values
            .Where(document => document.Path != path && document.Links.Any(held => FblLink.Parse(held.Text) is { } link && (link.DocumentName == name || (id is not null && link.DocumentId == id))))
            .Select(document => document.Path)];
    }

    /// <summary>How many links of the workspace a delete of <paramref name="elementIds"/> of the document at <paramref name="path"/> breaks (FBL 17.5).</summary>
    public int BrokenByDeleting(string path, IEnumerable<string> elementIds) => elementIds.Sum(id => Backlinks(path, id).Count);

    /// <summary>
    /// The lookup's search (DISL 0.12 §9.4, FBL 17.3): the linkable elements of the workspace's documents whose name holds
    /// <paramref name="query"/>, compared without regard to case, those it begins first, then by name and by path; only of the
    /// tool types <paramref name="origins"/> lists when it lists any, and only of documents with an id, which a link needs.
    /// An element that holds a link is not offered: it stands for its target, and a link to it would be a link to a link. At most <paramref name="limit"/> are given, each with the link that names it.
    /// </summary>
    public IReadOnlyList<LookupCandidate> Search(string query, IReadOnlyCollection<string>? origins = null, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(query);
        var typed = query.Trim();
        var found = new List<LookupCandidate>();
        foreach (var document in _documents.Values)
        {
            if (document.DocumentId is not { } documentId || (origins is { Count: > 0 } && (document.Origin is null || !origins.Contains(document.Origin)))) continue;
            var holders = document.Links.Select(link => link.Holder).ToHashSet(StringComparer.Ordinal);
            foreach (var element in document.Elements.Where(element => !holders.Contains(element.Id)))
            {
                (string? _, string name) = NameOf(document, element, null);
                if (name.Length == 0 || !name.Contains(typed, StringComparison.OrdinalIgnoreCase)) continue;
                found.Add(new LookupCandidate(document.Path, FolderOf(document.Path), document.FileName, element.Id, name, new FblLink(documentId, document.FileName, element.Id, null, name).Text));
            }
        }

        return [.. found
            .OrderBy(candidate => candidate.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Take(limit)];
    }

    /// <summary>The folder of <paramref name="path"/> as a finding names it, relative to the root; empty for the root itself.</summary>
    private string FolderOf(string path)
    {
        var relative = Display(path);
        var slash = relative.LastIndexOf('/');
        return slash < 0 ? "" : relative[..slash];
    }

    private static LinkResolution Finding(FblLink? link, string code, string message, IReadOnlyList<string> candidates) =>
        new(link, null, null, null, new LinkFinding(code, message, candidates));

    /// <summary>
    /// What names <paramref name="element"/> of <paramref name="document"/> (FBL 17.1): the value of <paramref name="nameProperty"/>
    /// when the element has that attribute, else of the type's name property, else of its first attribute; with the name
    /// property the link writes, null for the type's own, and in place of one that names no attribute, the one used (17.4).
    /// </summary>
    private static (string? NameProperty, string Name) NameOf(IndexedDocument document, IndexedElement element, string? nameProperty)
    {
        if (nameProperty is not null && element.ValueOf(nameProperty) is { } value) return (nameProperty, value);
        var own = (document.NameProperty is { } declared ? element.ValueOf(declared) : element.Attributes.Count > 0 ? element.Attributes[0].Value : null) ?? "";
        return (nameProperty is not null ? document.NameProperty : null, own);
    }
}
