using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Carries <see cref="WardleyRuleSet"/> to the Errors &amp; Warnings panel, and adds the one
/// rule that cannot be a pure function (Requirement 14.1).
/// </summary>
/// <remarks>
/// <para>
/// The rules themselves stay a pure function over a parsed map. This class parses, forwards,
/// and then judges the one thing a model cannot answer: whether a `url` that looks like a file
/// path is a file that is actually in the project (Requirement 14.6). That check needs the
/// project root, which arrives on the request - so it lives here rather than making the rule
/// set take a filesystem.
/// </para>
/// <para>
/// Core neither interprets, ranks nor rewrites what comes back (Requirement 14.1); it routes it
/// by origin and shows it.
/// </para>
/// </remarks>
public sealed class WardleyValidator : IDiagramValidator
{
    public const string SubmapMissingRuleId = "wardley.submap-missing";

    public DiagramOrigin Origin => Diagram.WardleyMap.Origin;

    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var map = WardleyParser.Parse(WardleyDocument.Parse(request.Document));
        var problems = new List<DiagramProblem>(WardleyRuleSet.Validate(map));
        problems.AddRange(UnresolvedReferences(map, request));

        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>
    /// A `url` whose address looks like a file in this project, and is not (Requirement 14.6).
    /// </summary>
    /// <remarks>
    /// <b>A warning, never an error, and only for an address that looks local.</b> An
    /// `https://` address may legitimately point outside the workspace, and reporting every one
    /// of those would make the panel useless for the case that matters - a submap naming a map
    /// that is genuinely not there.
    /// </remarks>
    private static IEnumerable<DiagramProblem> UnresolvedReferences(WardleyMap map, DiagramValidationRequest request)
    {
        foreach (var url in map.Urls)
        {
            if (!LooksLikeAFile(url.Address))
            {
                continue;
            }

            var full = IoPath.GetFullPath(IoPath.Combine(request.RootPath, url.Address.Replace('/', IoPath.DirectorySeparatorChar)));

            // Containment is core's job everywhere else, and it is this rule's here: a `..`
            // walking out of the project is not a reference this reports on, it is one it
            // refuses to go looking for.
            var root = IoPath.GetFullPath(request.RootPath);
            var inside = full.StartsWith(root, StringComparison.OrdinalIgnoreCase);

            if (!inside || File.Exists(full))
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
                SubmapMissingRuleId,
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
