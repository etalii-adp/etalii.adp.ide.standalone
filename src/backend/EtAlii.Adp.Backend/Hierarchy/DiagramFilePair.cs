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
    /// <summary>
    /// The sibling that <paramref name="adpPath"/>'s type declares, whether or not it exists
    /// on disk - or null when the file is not a registration file, cannot be read, names an
    /// unknown type, or names a type that keeps no sibling.
    /// </summary>
    public static string? SiblingOf(string adpPath, IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (!IsRegistrationFile(adpPath) || DefinitionOf(adpPath, catalog) is not { HasDocumentSibling: true } definition)
        {
            return null;
        }

        return SiblingPathFor(adpPath, definition.Extension);
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
