using EtAlii.Adp.Common;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The registrations that belong to one subject, and the header surgery a rename needs - shared
/// by the rename and delete commands so "which registrations are over this file" is answered by
/// exactly one implementation (adp-file-nesting Requirements 5 and 6).
/// </summary>
internal static class DiagramRegistrationSet
{
    /// <summary>Every registration in <paramref name="parentPath"/> whose body derives or names <paramref name="subjectPath"/>.</summary>
    internal static IEnumerable<string> Over(string parentPath, string subjectPath, IDiagramDefinitionCatalog catalog)
    {
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(parentPath, "*" + DiagramFileName.Extension);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        var subjectName = IoPath.GetFileName(subjectPath);
        foreach (var candidate in candidates)
        {
            // Name derivation, resolved against the folder itself; a body: header naming the
            // subject's file name counts too, which is how a command - which has no project
            // root to resolve a header path against - still finds header-pointed sets.
            var body = DiagramFilePair.BodyOf(candidate, catalog, parentPath);
            var derives = body is { } resolved && resolved.Path.Length > 0 &&
                          string.Equals(resolved.Path, subjectPath, StringComparison.OrdinalIgnoreCase);
            var headerNamesIt = SafeRead(candidate) is { } content &&
                                !string.Equals(RewriteBodyHeaderSegment(content, subjectName, subjectName + "\0"), content, StringComparison.Ordinal);
            if (derives || headerNamesIt)
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Rewrites a <c>body:</c> header whose final path segment is <paramref name="oldFileName"/>
    /// to name <paramref name="newFileName"/>, leaving everything else - including the rest of
    /// the header's relative path - untouched. Returns the content unchanged when no header
    /// matches.
    /// </summary>
    internal static string RewriteBodyHeaderSegment(string content, string oldFileName, string newFileName)
    {
        var lines = content.Split('\n');
        for (var index = 1; index < lines.Length && index <= 9; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!trimmed.StartsWith("body:", StringComparison.OrdinalIgnoreCase))
            {
                if (!trimmed.StartsWith("view:", StringComparison.OrdinalIgnoreCase))
                {
                    break; // past the header block
                }

                continue;
            }

            var value = trimmed["body:".Length..].Trim();
            var segment = value.Replace('\\', '/');
            var lastSlash = segment.LastIndexOf('/');
            var fileSegment = lastSlash < 0 ? value : value[(lastSlash + 1)..];
            if (!string.Equals(fileSegment.TrimEnd('\r'), oldFileName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var prefixLength = line.Length - line.TrimStart().Length;
            var directoryPart = lastSlash < 0 ? "" : value[..(lastSlash + 1)];
            var carriage = line.EndsWith('\r') ? "\r" : "";
            lines[index] = line[..prefixLength] + "body: " + directoryPart + newFileName + carriage;
            break;
        }

        return string.Join('\n', lines);
    }

    internal static string? SafeRead(string path)
    {
        try
        {
            return SharedDocumentReader.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
