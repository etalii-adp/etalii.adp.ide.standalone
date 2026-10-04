using EtAlii.Adp.Editor;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Which editor opens a file: an exact file-name claim first (<c>Makefile</c>), then the
/// extension, then the fallback. Groups are computed once at construction, so every conflict
/// in the deployment is reported at startup - once, at <c>Error</c> - and each affected file
/// degrades to <see cref="EditorAmbiguous"/> while the host keeps running
/// (modular-text-editors Requirements 3.2, 4.1-4.5).
/// </summary>
/// <remarks>
/// Deliberately NOT <c>DiagramValidators</c>'s construction-time throw, however familiar that
/// pattern is: two validators disagreeing about one diagram type's rules is contained to that
/// type, while two editors disagreeing about one extension would - if it threw - take the
/// whole host down, diagrams included, over a conflict in a family that is supposed to be
/// optional and additive. The design compared the two precedents and chose
/// <c>DiagramFileRouter</c>'s degrade-per-file, raised from <c>Warning</c> to <c>Error</c> so
/// "reported at startup" stays unmissable.
/// </remarks>
public sealed class EditorResolver
{
    private readonly Dictionary<string, EditorRouting> _byFileName;
    private readonly Dictionary<string, EditorRouting> _byExtension;
    private readonly Dictionary<string, IReadOnlyList<EditorDefinition>> _claimantsByExtension;
    private readonly EditorRouting? _fallback;

    public EditorResolver(IEditorDefinitionCatalog catalog)
        : this(catalog, Log.ForContext<EditorResolver>())
    {
    }

    /// <summary>
    /// The logger is injectable so the once-at-Error contract stays assertable: the shared
    /// static pipeline is replaced whenever an integration test builds a real host, which
    /// makes a global capture's view of these lines depend on test ordering.
    /// </summary>
    internal EditorResolver(IEditorDefinitionCatalog catalog, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        var definitions = catalog.All;
        _byFileName = ResolveGroups(definitions, definition => definition.FileNames, "file name", logger);
        _byExtension = ResolveGroups(definitions, definition => definition.Extensions, "extension", logger);
        _claimantsByExtension = definitions
            .SelectMany(definition => definition.Extensions.Select(extension => (Extension: extension, Definition: definition)))
            .GroupBy(claim => claim.Extension, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                IReadOnlyList<EditorDefinition> (group) =>
                    [.. group.Select(claim => claim.Definition).OrderBy(definition => definition.Id, StringComparer.Ordinal)],
                StringComparer.Ordinal);
        _fallback = ResolveFallback(definitions, logger);
    }

    /// <summary>
    /// Every editor claiming <paramref name="path"/>'s extension, ordered by id - what
    /// "Open with…" lists (Requirement 4.4). One entry for a solely-claimed extension, empty
    /// for a file only the fallback would answer; <see cref="Resolve"/> stays the authority
    /// on which of them actually opens on activation.
    /// </summary>
    public IReadOnlyList<EditorDefinition> ClaimantsOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var extension = IoPath.GetExtension(path).ToLowerInvariant();
        return extension.Length > 0 && _claimantsByExtension.TryGetValue(extension, out var claimants) ? claimants : [];
    }

    /// <summary>
    /// Whether a non-fallback editor claims <paramref name="path"/> - by exact file name or
    /// by extension, the ambiguous case included: the test is whether an editor claims the
    /// file, not whether opening it will succeed, and the fallback claims everything by
    /// construction so it never counts (small-refinements Requirement 3.2). Kept beside
    /// <see cref="Resolve"/> so the precedence lives in one place: Resolve never answers
    /// null-like, so a caller could not tell "claimed" from "fell through" without
    /// re-implementing it.
    /// </summary>
    public bool IsClaimed(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fileName = IoPath.GetFileName(path);
        if (_byFileName.ContainsKey(fileName))
        {
            return true;
        }

        var extension = IoPath.GetExtension(fileName).ToLowerInvariant();
        return extension.Length > 0 && _byExtension.ContainsKey(extension);
    }

    /// <summary>
    /// The editor for <paramref name="path"/>. Never null-like: the fallback answers when
    /// nothing claims the file, which is its entire role.
    /// </summary>
    public EditorRouting Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fileName = IoPath.GetFileName(path);
        if (_byFileName.TryGetValue(fileName, out var byName))
        {
            return byName;
        }

        // Lower-cased to match EditorDefinition's by-construction normalisation, so ".TXT"
        // on disk and ".txt" in a definition are the same claim (Requirement 2.2).
        var extension = IoPath.GetExtension(fileName).ToLowerInvariant();
        if (extension.Length > 0 && _byExtension.TryGetValue(extension, out var byExtension))
        {
            return byExtension;
        }

        return _fallback ?? new EditorAmbiguous(extension, []);
    }

    /// <summary>
    /// One routing outcome per key, decided up front: a single claimant routes; several with
    /// exactly one declared default route to the default (Requirement 4.4's legitimate case);
    /// several without one - or with more than one, which is just as undecidable - log once
    /// and stay ambiguous.
    /// </summary>
    private static Dictionary<string, EditorRouting> ResolveGroups(
        IReadOnlyList<EditorDefinition> definitions,
        Func<EditorDefinition, IReadOnlyList<string>> keysOf,
        string keyKind,
        ILogger logger)
    {
        var groups = new Dictionary<string, List<EditorDefinition>>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            foreach (var key in keysOf(definition))
            {
                if (!groups.TryGetValue(key, out var claimants))
                {
                    groups[key] = claimants = [];
                }

                claimants.Add(definition);
            }
        }

        var resolved = new Dictionary<string, EditorRouting>(StringComparer.Ordinal);
        foreach ((string key, List<EditorDefinition> claimants) in groups)
        {
            if (claimants.Count == 1)
            {
                resolved[key] = new EditorRouted(claimants[0]);
                continue;
            }

            var defaults = claimants.Where(claimant => claimant.IsDefaultForSharedExtension).ToArray();
            if (defaults.Length == 1)
            {
                resolved[key] = new EditorRouted(defaults[0]);
                continue;
            }

            var ordered = claimants.OrderBy(claimant => claimant.Id, StringComparer.Ordinal).ToArray();
            logger.Error(
                "Editors {Ids} all claim the {KeyKind} '{Key}' and {Reason}; every such file will report the conflict instead of opening",
                string.Join(", ", ordered.Select(claimant => claimant.Id)),
                keyKind,
                key,
                defaults.Length == 0 ? "none is the declared default" : "more than one claims to be the default");
            resolved[key] = new EditorAmbiguous(key, ordered);
        }

        return resolved;
    }

    /// <summary>
    /// The one definition that answers when nothing claims a file. A deployment without one -
    /// or with rivals for the role - is broken in a way worth one <c>Error</c> at startup,
    /// never a crash.
    /// </summary>
    private static EditorRouting? ResolveFallback(IReadOnlyList<EditorDefinition> definitions, ILogger logger)
    {
        var fallbacks = definitions.Where(definition => definition.IsFallback)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        switch (fallbacks.Length)
        {
            case 1:
                return new EditorRouted(fallbacks[0]);
            case 0:
                logger.Error(
                    "No editor declares IsFallback; files nothing claims will report a conflict instead of opening as plain text");
                return null;
            default:
                logger.Error(
                    "Editors {Ids} all declare IsFallback; keeping {Kept}",
                    string.Join(", ", fallbacks.Select(fallback => fallback.Id)),
                    fallbacks[0].Id);
                return new EditorRouted(fallbacks[0]);
        }
    }
}
