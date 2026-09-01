namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// How a registration file names the subject it belongs to: <c>subject.adp</c> for the single
/// registration, <c>subject.qualifier.adp</c> when a subject carries several, and <c>.adp</c>
/// exactly - the extension with no base name - for a registration belonging to its folder
/// (adp-file-nesting Requirement 1).
/// </summary>
/// <remarks>
/// <para>
/// The parse is deliberately *shape only*. <c>my.config.adp</c> is a subject <c>my.config</c>
/// with no qualifier, and also a subject <c>my</c> with qualifier <c>config</c>, and nothing in
/// the name says which. This type reports both readings and refuses to choose; deciding needs to
/// know what is on disk, which is <see cref="DiagramFilePair"/>'s job (Requirement 2.2).
/// </para>
/// <para>
/// A folder-scoped name has no base to derive a sibling from, which is why it is a distinct
/// state here rather than a base name that happens to be empty - Requirement 4.4 asks for that
/// case to be excluded from sibling derivation, and excluding it by construction is harder to
/// forget than excluding it by a check at every call site.
/// </para>
/// </remarks>
public sealed record DiagramRegistrationName
{
    private DiagramRegistrationName(string fullBase, string subjectBase, string qualifier, bool isFolderScoped)
    {
        FullBase = fullBase;
        SubjectBase = subjectBase;
        Qualifier = qualifier;
        IsFolderScoped = isFolderScoped;
    }

    /// <summary>Everything before the extension, as written. Empty for a folder-scoped name.</summary>
    public string FullBase { get; }

    /// <summary>
    /// <see cref="FullBase"/> with its last dot-separated segment removed when there is one to
    /// remove - the subject reading of an ambiguous name.
    /// </summary>
    public string SubjectBase { get; }

    /// <summary>The last dot-separated segment, or empty when the name carries none.</summary>
    public string Qualifier { get; }

    /// <summary>Whether this is the bare <c>.adp</c> that registers its containing folder.</summary>
    public bool IsFolderScoped { get; }

    /// <summary>Whether the name offers a qualified reading at all.</summary>
    public bool IsQualified => Qualifier.Length > 0;

    /// <summary>
    /// Parses <paramref name="fileName"/> - a file name, not a path - or returns null when it is
    /// not a registration file.
    /// </summary>
    public static DiagramRegistrationName? TryParse(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (!fileName.EndsWith(DiagramFileName.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fullBase = fileName[..^DiagramFileName.Extension.Length];
        if (fullBase.Length == 0)
        {
            return new DiagramRegistrationName("", "", "", isFolderScoped: true);
        }

        // The last dot splits a possible qualifier off. A leading dot is not a separator here:
        // `.hidden.adp` reads as the subject `.hidden`, because a subject may legitimately be a
        // dot-file and a qualifier may not be empty.
        var separator = fullBase.LastIndexOf('.');
        return separator <= 0
            ? new DiagramRegistrationName(fullBase, fullBase, "", isFolderScoped: false)
            : new DiagramRegistrationName(fullBase, fullBase[..separator], fullBase[(separator + 1)..], isFolderScoped: false);
    }

    /// <summary>The file name for a subject and an optional qualifier, with the qualifier sanitised.</summary>
    /// <remarks>
    /// The qualifier goes through the same sanitiser a base name already does, so it cannot
    /// introduce a path separator or a character the filesystem refuses (Requirement 1.4). That
    /// sanitiser also replaces a period, which keeps a composed name unambiguous about where its
    /// qualifier begins.
    /// </remarks>
    public static string Compose(string subjectBase, string qualifier)
    {
        ArgumentNullException.ThrowIfNull(subjectBase);
        ArgumentNullException.ThrowIfNull(qualifier);

        var sanitised = qualifier.Trim();
        return sanitised.Length == 0
            ? subjectBase + DiagramFileName.Extension
            : $"{subjectBase}.{DiagramFileName.Sanitise(sanitised)}{DiagramFileName.Extension}";
    }

    /// <summary>The name of a registration belonging to its containing folder.</summary>
    public static string ForFolder() => DiagramFileName.Extension;
}
