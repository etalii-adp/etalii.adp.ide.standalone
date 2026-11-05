using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// <c>wardley.submap-missing</c>, the one finding of a Wardley map that stays module code: it stands in for gap 9 of
/// wardley-map.md (finding A5), DISL having no way to resolve an address against the project's root, so the definition
/// does not state the rule. It is <c>WardleyValidator.UnresolvedReferences</c> moved as it was, resolving against the
/// root of the request.
/// </summary>
internal static class WardleySubmapRule
{
    public const string RuleId = "wardley.submap-missing";

    /// <summary>
    /// A `url` whose address looks like a file in this project, and is not (Requirement 14.6).
    /// </summary>
    /// <remarks>
    /// <b>A warning, never an error, and only for an address that looks local.</b> An
    /// `https://` address may legitimately point outside the workspace, and reporting every one
    /// of those would make the panel useless for the case that matters - a submap naming a map
    /// that is genuinely not there.
    /// </remarks>
    public static IEnumerable<DiagramProblem> UnresolvedReferences(WardleyMap map, DiagramValidationRequest request)
    {
        foreach (var url in map.Urls)
        {
            if (!LooksLikeAFile(url.Address))
            {
                continue;
            }

            // Containment is the project root's to judge: a `..` walking out of the project is
            // not a reference this reports on, it is one it refuses to go looking for. Nor is the
            // root itself, which no address names as a file.
            if (request.Root.Resolve(url.Address.Replace('/', IoPath.DirectorySeparatorChar)) is not { RelativePath: not "." } target
                || File.Exists(target.FullPath))
            {
                continue;
            }

            var referrers = map.Components
                .Where(component => component.Url == url.Name)
                .Select(component => component.Name)
                .ToArray();

            var named = referrers.Length > 0 ? $"'{referrers[0]}' points at" : "This map defines";

            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"{named} '{url.Address}', which is not in this project.",
                RuleId,
                new DiagramProblemLineLocation(url.Line));
        }
    }

    /// <summary>
    /// Whether an address is a path in this project rather than somewhere on the internet.
    /// </summary>
    /// <remarks>
    /// Deliberately conservative: anything carrying a scheme is left alone. Guessing that
    /// `mailto:` or `obsidian://` is a missing file would produce a warning nobody can act on.
    /// </remarks>
    private static bool LooksLikeAFile(string address) =>
        address.Length > 0 &&
        !address.Contains("://", StringComparison.Ordinal) &&
        !address.Contains(':', StringComparison.Ordinal);
}
