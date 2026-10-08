using System.Reflection;
using Serilog;

namespace EtAlii.Adp.Editor;

/// <summary>
/// Finds every text editor the application carries by looking, in each of its own assemblies,
/// for a static class named <c>Editor</c> with a public static <c>Definitions</c> property
/// holding <see cref="EditorDefinition"/>s - the same scan the diagram family runs for its
/// <c>Diagram</c> classes, extended rather than duplicated (modular-text-editors
/// Requirement 1.3): the mechanical half is the shared <c>ToolDefinitionScan</c>, and the
/// assembly walk is <see cref="ApplicationAssemblies.Find"/> itself.
/// </summary>
/// <remarks>
/// It never throws for a bad assembly or a bad candidate. A module that cannot be loaded or
/// that declares its definition wrongly costs exactly its own entries and a warning in the
/// log - never the application's startup.
/// </remarks>
public static class EditorDefinitionDiscovery
{
    private const string CandidateTypeName = "Editor";
    private const string DefinitionsPropertyName = "Definitions";

    private static readonly ILogger _logger = Log.ForContext(typeof(EditorDefinitionDiscovery));

    /// <summary>Scans the application's own assemblies, found by the shared walk.</summary>
    public static IReadOnlyList<EditorDefinition> Discover()
    {
        var assemblies = ApplicationAssemblies.Find();
        return Discover(assemblies);
    }

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, ordered by id so
    /// the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<EditorDefinition> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var scan = ToolDefinitionScan.Scan<EditorDefinition>(assemblies, CandidateTypeName, DefinitionsPropertyName, "editor", _logger);

        // Keyed by id so a duplicate is detected as it arrives; the value remembers which
        // assembly won so a later collision can be reported against it.
        var found = new Dictionary<string, (EditorDefinition Definition, string AssemblyName)>(StringComparer.Ordinal);

        foreach (var hit in scan.Found)
        {
            var definition = hit.Definition;
            var assemblyName = hit.AssemblyName;

            if (found.TryGetValue(definition.Id, out var existing))
            {
                // Keep the ordinal-smaller assembly name so the winner does not depend on the
                // order assemblies happened to be handed in - the diagram family's rule.
                var keepExisting = string.CompareOrdinal(existing.AssemblyName, assemblyName) <= 0;
                var kept = keepExisting ? existing.AssemblyName : assemblyName;
                var dropped = keepExisting ? assemblyName : existing.AssemblyName;
                _logger.Warning(
                    "Editor id {Id} is declared more than once; dropping {DroppedAssembly} and keeping the one from {KeptAssembly}",
                    definition.Id,
                    dropped,
                    kept);

                if (!keepExisting)
                {
                    found[definition.Id] = (definition, assemblyName);
                }

                continue;
            }

            found[definition.Id] = (definition, assemblyName);
            _logger.Information(
                "Discovered editor {Id}: {Title} ({Assembly})",
                definition.Id,
                definition.Title,
                assemblyName);
        }

        var result = found.Values
            .Select(entry => entry.Definition)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        _logger.Information(
            "{Count} editors discovered across {AssemblyCount} assemblies",
            result.Length,
            scan.AssembliesScanned);
        if (result.Length == 0)
        {
            _logger.Warning(
                "No editors were discovered; files no diagram claims will not open. This usually means the editor modules were not deployed or not referenced");
        }

        return result;
    }
}
