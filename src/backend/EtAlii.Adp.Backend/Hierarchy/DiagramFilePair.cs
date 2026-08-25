using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The one place that knows an <c>.adp</c> registration file may have a document sibling:
/// <c>&lt;name&gt;.adp</c> whose first line names a type with an
/// <see cref="DiagramDefinition.Extension"/>, and <c>&lt;name&gt;&lt;extension&gt;</c> beside it.
/// Rename, delete and open all ask here, so the pair is defined once (mindmap-diagram
/// Requirement 2.1).
/// </summary>
public static class DiagramFilePair
{
    /// <summary>The header naming a body document this registration does not own.</summary>
    private const string BodyHeader = "body:";

    /// <summary>The header naming which view within that document this registration opens.</summary>
    private const string ViewHeader = "view:";

    /// <summary>How many lines after the MIME line are scanned for headers before giving up.</summary>
    private const int HeaderScanLimit = 8;

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
    /// <param name="projectRoot">
    /// The project folder a <c>body:</c> header is resolved against and must stay inside. Null
    /// means "no header may be followed": the caller only wants the owned sibling.
    /// </param>
    public static DiagramBodyFile? BodyOf(string adpPath, IDiagramDefinitionCatalog catalog, string? projectRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (!IsRegistrationFile(adpPath) || DefinitionOf(adpPath, catalog) is not { HasDocumentSibling: true } definition)
        {
            return null;
        }

        var (body, view) = ReadBodyAndViewHeaders(adpPath);
        if (body is null)
        {
            // No header: the body is the sibling of this file's own name, exactly as every
            // type has always behaved. A view key without a body still applies - one
            // registration beside its own document may still name a view within it.
            return new DiagramBodyFile(SiblingPathFor(adpPath, definition.Extension), view, IsOwned: true);
        }

        if (projectRoot is null)
        {
            // The caller is asking about ownership only - a named body is never owned - and
            // resolving the path would need the project root it did not supply. Path is empty
            // rather than wrong; nothing reads it when IsOwned is false.
            return new DiagramBodyFile(Path: string.Empty, view, IsOwned: false);
        }

        var resolved = ResolveWithin(projectRoot, body);
        return resolved is null ? null : new DiagramBodyFile(resolved, view, IsOwned: false);
    }

    /// <summary>
    /// <paramref name="relativePath"/> resolved against <paramref name="projectRoot"/>, or null
    /// when it escapes it. A body outside the project is refused rather than read: the header
    /// is user-editable text, and following it anywhere on disk would make an <c>.adp</c> file
    /// a way to read arbitrary files through the backend.
    /// </summary>
    private static string? ResolveWithin(string projectRoot, string relativePath)
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
            fullPath = IoPath.GetFullPath(IoPath.Combine(fullRoot, relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var normalizedRoot = fullRoot.EndsWith(IoPath.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + IoPath.DirectorySeparatorChar;

        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    /// <summary>
    /// The <c>body:</c> and <c>view:</c> headers following the MIME line, or nulls. Scanning
    /// stops at the first line that is neither a header nor blank, so a document that happens
    /// to start with prose is not searched to its end.
    /// </summary>
    private static (string? Body, string? View) ReadBodyAndViewHeaders(string adpPath)
    {
        try
        {
            using var reader = new StreamReader(adpPath);
            reader.ReadLine(); // the MIME line, which DefinitionOf has already used

            string? body = null;
            string? view = null;
            for (var scanned = 0; scanned < HeaderScanLimit; scanned++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith(BodyHeader, StringComparison.OrdinalIgnoreCase))
                {
                    body = Value(trimmed, BodyHeader);
                }
                else if (trimmed.StartsWith(ViewHeader, StringComparison.OrdinalIgnoreCase))
                {
                    view = Value(trimmed, ViewHeader);
                }
                else
                {
                    break;
                }
            }

            return (body, view);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }

        static string? Value(string line, string header)
        {
            var value = line[header.Length..].Trim();
            return value.Length == 0 ? null : value;
        }
    }

    /// <summary>The sibling path a registration file at <paramref name="adpPath"/> would have for <paramref name="extension"/>.</summary>
    public static string SiblingPathFor(string adpPath, string extension) =>
        IoPath.Combine(IoPath.GetDirectoryName(adpPath) ?? "", DiagramFileName.StripExtension(IoPath.GetFileName(adpPath)) + extension);

    /// <summary>
    /// The type a registration file declares, from its first line, or null when the file
    /// cannot be read or the MIME type matches no discovered definition.
    /// </summary>
    public static DiagramDefinition? DefinitionOf(string adpPath, IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var mimeType = ReadMimeType(adpPath);
        return mimeType is null
            ? null
            : catalog.All.FirstOrDefault(definition => string.Equals(definition.Origin.MimeType, mimeType, StringComparison.Ordinal));
    }

    /// <summary>The first line of a registration file, trimmed, or null when it cannot be read.</summary>
    public static string? ReadMimeType(string adpPath)
    {
        try
        {
            using var reader = new StreamReader(adpPath);
            return reader.ReadLine()?.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsRegistrationFile(string path) =>
        path.EndsWith(DiagramFileName.Extension, StringComparison.OrdinalIgnoreCase);
}
