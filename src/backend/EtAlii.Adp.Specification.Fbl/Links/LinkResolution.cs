namespace EtAlii.Adp.Specification.Fbl.Links;

/// <summary>The codes of a link's findings (FBL 17.6).</summary>
public static class LinkCodes
{
    /// <summary>A value that is not a link.</summary>
    public const string Malformed = "link.malformed";
    /// <summary>A link whose document is not in the workspace.</summary>
    public const string BrokenDocument = "link.broken-document";
    /// <summary>A link whose element is no longer in its document.</summary>
    public const string BrokenElement = "link.broken-element";
    /// <summary>A link that more than one document, or more than one element, answers.</summary>
    public const string AmbiguousDocument = "link.ambiguous-document";
}

/// <summary>
/// What resolving a link gave (FBL 17.3): the target document and, for a link to an element, that element, with the link as
/// load-time validation writes it (17.4, step 4); or the finding that stands instead (17.6).
/// </summary>
public sealed record LinkResolution(FblLink? Link, IndexedDocument? Document, IndexedElement? Element, FblLink? Refreshed, LinkFinding? Finding)
{
    /// <summary>Whether the link resolved, to its document or to an element of it.</summary>
    public bool Resolved => Finding is null;
}

/// <summary>
/// A link's finding (FBL 17.6): its code, its sentence, and for <c>link.ambiguous-document</c> the documents to choose from,
/// by path.
/// </summary>
public sealed record LinkFinding(string Code, string Message, IReadOnlyList<string> Candidates);

/// <summary>A link, in some document of the workspace, that resolves to an element (FBL 17.5): the consuming document, and the element or value that holds it.</summary>
public sealed record Backlink(string Path, string Holder);
