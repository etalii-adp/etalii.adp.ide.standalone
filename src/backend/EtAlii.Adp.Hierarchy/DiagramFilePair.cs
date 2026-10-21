using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The one place that knows an <c>.adp</c> registration file may have a document sibling:
/// <c>&lt;name&gt;.adp</c> whose first line names a type with an
/// <see cref="DiagramDefinition.Extension"/>, and <c>&lt;name&gt;&lt;extension&gt;</c> beside it.
/// Rename, delete and open all ask here, so the pair is defined once (mindmap-diagram
/// Requirement 2.1).
/// </summary>
public static class DiagramFilePair
{
    /// <summary>
    /// The body <paramref name="adpPath"/> **owns** - the sibling derived from its own name -
    /// or null when the file is not a registration file, cannot be read, names an unknown type,
    /// names a type that keeps no sibling, **or names a body it does not own**.
    /// </summary>
    /// <remarks>
    /// Delete and rename ask here, so a shared body is never carried off by one of the
    /// registrations that merely reference it. To resolve the body to *open*, owned or not,
    /// ask <see cref="BodyOf"/>.
    /// </remarks>
    public static string? SiblingOf(string adpPath, IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var body = BodyOf(adpPath, catalog, projectRoot: null);
        return body is { IsOwned: true } owned ? owned.Path : null;
    }

    /// <summary>
    /// The body <paramref name="adpPath"/> opens: the document its <c>body:</c> header names
    /// when it has one, and the derived sibling otherwise. Null under the same conditions
    /// <see cref="SiblingOf"/> returns null, minus the ownership rule - and also when a
    /// <c>body:</c> header points outside <paramref name="projectRoot"/>, which is refused
    /// rather than followed (Requirement 2.4).
    /// </summary>
    /// <remarks>
    /// A <c>body:</c> header is relative to the registration file's OWN folder, not to the
    /// project root: a body beside its registration is just its file name, and moving the
    /// pair together never invalidates the header. <c>..</c> may climb within the project;
    /// the containment rule still refuses anything that escapes it.
    /// </remarks>
    /// <param name="catalog">The diagram definition catalog.</param>
    /// <param name="projectRoot">
    /// The project folder a resolved <c>body:</c> header must stay inside. Null means "no
    /// header may be followed": the caller only wants the owned sibling.
    /// </param>
    /// <param name="adpPath">The path to the diagram registration file.</param>
    public static DiagramBodyFile? BodyOf(string adpPath, IDiagramDefinitionCatalog catalog, string? projectRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (!IsRegistrationFile(adpPath)
            || AdpRegistration.Read(adpPath) is not { } registration
            || DefinitionOf(registration, catalog) is not { HasDocumentSibling: true } definition)
        {
            return null;
        }

        return BodyOf(adpPath, registration, definition, projectRoot);
    }

    /// <summary>
    /// <see cref="BodyOf(string, IDiagramDefinitionCatalog, string?)"/> for a registration already read
    /// and a type already known to keep a sibling, so a route reads the file once (code-structure T174).
    /// </summary>
    internal static DiagramBodyFile? BodyOf(string adpPath, AdpRegistration registration, DiagramDefinition definition, string? projectRoot)
    {
        var body = registration.Body;
        var view = registration.View;
        if (body is null)
        {
            // No header: the body is the sibling of this file's own name, exactly as every
            // type has always behaved. A view key without a body still applies - one
            // registration beside its own document may still name a view within it.
            (string path, string? ambiguousWith, bool subjectIsShared) = ResolveSibling(adpPath, definition.Extension, definition.AlternateExtension);

            // A folder-scoped registration has no derived body at all, rather than one whose path
            // is the bare extension (Requirement 4.4).
            //
            // Ownership is per SET, not per pair (Requirement 2.4): a qualified registration
            // derives a subject that other registrations may also derive, so it opens the body
            // but never carries it - only the unqualified reading, where the name IS the
            // subject, keeps the classic pair behaviour of taking its sibling along on delete
            // and rename (Requirements 6.2 and 11.1).
            return path.Length == 0
                ? null
                : new DiagramBodyFile(path, view, IsOwned: !subjectIsShared, AmbiguousWith: ambiguousWith);
        }

        if (projectRoot is null)
        {
            // The caller is asking about ownership only - a named body is never owned - and
            // resolving the path would need the project root it did not supply. Path is empty
            // rather than wrong; nothing reads it when IsOwned is false.
            return new DiagramBodyFile(Path: string.Empty, view, IsOwned: false);
        }

        var resolved = ResolveWithin(projectRoot, IoPath.GetDirectoryName(adpPath) ?? projectRoot, body);
        return resolved is null ? null : new DiagramBodyFile(resolved, view, IsOwned: false);
    }

    /// <summary>
    /// <paramref name="relativePath"/> resolved against <paramref name="baseDirectory"/> - the
    /// registration's own folder - or null when the result escapes
    /// <paramref name="projectRoot"/>. A body outside the project is refused rather than read:
    /// the header is user-editable text, and following it anywhere on disk would make an
    /// <c>.adp</c> file a way to read arbitrary files through the backend.
    /// </summary>
    internal static string? ResolveWithin(string projectRoot, string baseDirectory, string relativePath)
    {
        if (IoPath.IsPathRooted(relativePath))
        {
            return null;
        }

        string fullPath;
        string fullRoot;
        try
        {
            fullRoot = IoPath.GetFullPath(projectRoot);
            fullPath = IoPath.GetFullPath(IoPath.Combine(IoPath.GetFullPath(baseDirectory), relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return PathContainment.IsInside(fullRoot, fullPath) ? fullPath : null;
    }

    /// <summary>The sibling path a registration file at <paramref name="adpPath"/> would have for <paramref name="extension"/>.</summary>
    public static string SiblingPathFor(string adpPath, string extension) => ResolveSibling(adpPath, extension, alternate: "").Path;

    /// <summary>
    /// The body a registration file at <paramref name="adpPath"/> without a <c>body:</c> header would open for
    /// <paramref name="definition"/>: its primary extension's sibling, or its alternate's where only that one exists.
    /// </summary>
    public static string SiblingPathFor(string adpPath, DiagramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ResolveSibling(adpPath, definition.Extension, definition.AlternateExtension).Path;
    }

    /// <summary>
    /// The body a registration's own name derives, and the other candidate when the name is
    /// ambiguous about which it meant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is Requirement 2's: an unqualified name derives its base; a qualified name
    /// derives the subject the qualifier hangs off; and where a qualified-shaped name could mean
    /// either - a subject genuinely called <c>my.config</c>, or the subject <c>my</c> with the
    /// qualifier <c>config</c> - the longer candidate wins if it exists on disk, and the other
    /// is reported so the ambiguity can be said out loud rather than guessed at silently.
    /// </para>
    /// <para>
    /// Existence deciding a resolution is a real cost, entered deliberately (Requirement 2.2): a
    /// file created later can change what an existing registration derives. It is confined to
    /// hand-authored names, because a qualified registration ADP writes carries an explicit
    /// <c>body:</c> header, and Requirement 2.3 makes the header win before this runs at all.
    /// </para>
    /// <para>
    /// A folder-scoped registration derives nothing. Its name has no base, so a derivation would
    /// produce a path that is just the extension - which Requirement 4.4 asks to be excluded
    /// rather than produced.
    /// </para>
    /// <para>
    /// A type with an <see cref="DiagramDefinition.AlternateExtension"/> derives each reading's body
    /// as the first of the two extensions whose file exists, the primary first, and the primary's
    /// name when neither does - FBL 8.2's "the first that exists, in the order of
    /// <c>claims.extensions</c>" (dotnet-dependency-graph-disl-fbl task 9, ruled 2026-10-11). A
    /// registration beside a lone file of the alternate therefore keeps opening it when the alternate
    /// was the primary before; beside both, it opens the primary's.
    /// </para>
    /// </remarks>
    private static (string Path, string? AmbiguousWith, bool SubjectIsShared) ResolveSibling(string adpPath, string extension, string alternate)
    {
        ArgumentNullException.ThrowIfNull(adpPath);
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(alternate);

        var name = DiagramRegistrationName.TryParse(IoPath.GetFileName(adpPath));
        if (name is null || name.IsFolderScoped)
        {
            return (string.Empty, null, false);
        }

        var directory = IoPath.GetDirectoryName(adpPath) ?? "";
        var subject = Existing(directory, name.SubjectBase, extension, alternate);
        if (!name.IsQualified)
        {
            return (subject ?? IoPath.Combine(directory, name.SubjectBase + extension), null, false);
        }

        return Existing(directory, name.FullBase, extension, alternate) is not { } whole
            // The qualified reading won: the derived subject is shared by construction, since
            // any other qualifier over the same subject derives the same file.
            ? (subject ?? IoPath.Combine(directory, name.SubjectBase + extension), null, true)
            // Both readings name a real file, so the name alone cannot say which was meant. The
            // longer wins, and that is the unqualified reading - the name IS that subject.
            : (whole, subject, false);
    }

    /// <summary>
    /// The file <paramref name="baseName"/> names in <paramref name="directory"/> under <paramref name="extension"/>,
    /// else under <paramref name="alternate"/> when there is one, whichever exists first; null when neither does.
    /// </summary>
    private static string? Existing(string directory, string baseName, string extension, string alternate)
    {
        var primary = IoPath.Combine(directory, baseName + extension);
        if (File.Exists(primary))
        {
            return primary;
        }

        if (alternate.Length == 0)
        {
            return null;
        }

        var other = IoPath.Combine(directory, baseName + alternate);
        return File.Exists(other) ? other : null;
    }

    /// <summary>
    /// The type a registration file declares, from its first line, or null when the file
    /// cannot be read or the MIME type matches no discovered definition.
    /// </summary>
    public static DiagramDefinition? DefinitionOf(string adpPath, IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return AdpRegistration.Read(adpPath) is { } registration ? DefinitionOf(registration, catalog) : null;
    }

    /// <summary>The type <paramref name="registration"/> declares, or null when its MIME type matches no discovered definition.</summary>
    private static DiagramDefinition? DefinitionOf(AdpRegistration registration, IDiagramDefinitionCatalog catalog) =>
        registration.MimeType is { } mimeType
            ? catalog.All.FirstOrDefault(definition => string.Equals(definition.Origin.MimeType, mimeType, StringComparison.Ordinal))
            : null;

    public static bool IsRegistrationFile(string path) =>
        path.EndsWith(DiagramFileName.Extension, StringComparison.OrdinalIgnoreCase);
}
