using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The registrations that belong to one subject - shared
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
            var derives = body is { Path.Length: > 0 } resolved &&
                          string.Equals(resolved.Path, subjectPath, StringComparison.OrdinalIgnoreCase);
            var headerNamesIt = AdpRegistration.Read(candidate) is { } registration &&
                                !string.Equals(registration.WithBodyFileName(subjectName, subjectName + "\0"), registration.Text, StringComparison.Ordinal);
            if (derives || headerNamesIt)
            {
                yield return candidate;
            }
        }
    }
}
