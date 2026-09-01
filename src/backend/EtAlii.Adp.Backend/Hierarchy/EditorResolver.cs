using EtAlii.Adp.Editor;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

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
    private static readonly ILogger _logger = Log.ForContext<EditorResolver>();

    private readonly Dictionary<string, EditorRouting> _byFileName;
    private readonly Dictionary<string, EditorRouting> _byExtension;
    private readonly EditorRouting? _fallback;

    public EditorResolver(IEditorDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var definitions = catalog.All;
        _byFileName = ResolveGroups(definitions, definition => definition.FileNames, "file name");
        _byExtension = ResolveGroups(definitions, definition => definition.Extensions, "extension");
        _fallback = ResolveFallback(definitions);
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
        string keyKind)
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
        foreach (var (key, claimants) in groups)
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
            _logger.Error(
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
    private static EditorRouting? ResolveFallback(IReadOnlyList<EditorDefinition> definitions)
    {
        var fallbacks = definitions.Where(definition => definition.IsFallback)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        switch (fallbacks.Length)
        {
            case 1:
                return new EditorRouted(fallbacks[0]);
            case 0:
                _logger.Error(
                    "No editor declares IsFallback; files nothing claims will report a conflict instead of opening as plain text");
                return null;
            default:
                _logger.Error(
                    "Editors {Ids} all declare IsFallback; keeping {Kept}",
                    string.Join(", ", fallbacks.Select(fallback => fallback.Id)),
                    fallbacks[0].Id);
                return new EditorRouted(fallbacks[0]);
        }
    }
}
