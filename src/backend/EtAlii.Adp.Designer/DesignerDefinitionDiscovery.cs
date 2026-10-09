using System.Reflection;
using Serilog;

namespace EtAlii.Adp.Designer;

/// <summary>
/// Finds the designer types the application carries, by the same scan the diagram and editor
/// families run: in each of the application's own assemblies, a static class named
/// <c>Designer</c> with a public static <c>Definitions</c> property holding
/// <see cref="DesignerDefinition"/>s. The mechanical half is the shared
/// <c>ToolDefinitionScan</c>, and the assembly walk is <see cref="ApplicationAssemblies.Find"/>.
/// </summary>
/// <remarks>
/// It never throws for a bad assembly or a bad candidate. A module that cannot be loaded or
/// that declares its definition wrongly costs exactly its own entries and a warning in the
/// log - never the application's startup. Finding no designer is not a warning: an
/// application without a designer module is complete.
/// </remarks>
public static class DesignerDefinitionDiscovery
{
    private const string CandidateTypeName = "Designer";
    private const string DefinitionsPropertyName = "Definitions";

    private static readonly ILogger _logger = Log.ForContext(typeof(DesignerDefinitionDiscovery));

    /// <summary>Scans the application's own assemblies, found by the shared walk.</summary>
    public static IReadOnlyList<DesignerDefinition> Discover() => Discover(ApplicationAssemblies.Find());

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, one per origin,
    /// ordered by origin so the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<DesignerDefinition> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var scan = ToolDefinitionScan.Scan<DesignerDefinition>(assemblies, CandidateTypeName, DefinitionsPropertyName, "designer", _logger);

        // Keyed by origin so a duplicate is detected as it arrives; the value remembers which
        // assembly won so a later collision can be reported against it.
        var found = new Dictionary<string, (DesignerDefinition Definition, string AssemblyName)>(StringComparer.Ordinal);

        foreach (var hit in scan.Found)
        {
            var definition = hit.Definition;
            var assemblyName = hit.AssemblyName;

            if (found.TryGetValue(definition.Origin, out var existing))
            {
                // Keep the ordinal-smaller assembly name so the winner does not depend on the
                // order assemblies happened to be handed in - the other families' rule.
                var keepExisting = string.CompareOrdinal(existing.AssemblyName, assemblyName) <= 0;
                var kept = keepExisting ? existing.AssemblyName : assemblyName;
                var dropped = keepExisting ? assemblyName : existing.AssemblyName;
                _logger.Warning(
                    "Designer origin {Origin} is declared more than once; dropping {DroppedAssembly} and keeping the one from {KeptAssembly}",
                    definition.Origin,
                    dropped,
                    kept);

                if (!keepExisting)
                {
                    found[definition.Origin] = (definition, assemblyName);
                }

                continue;
            }

            found[definition.Origin] = (definition, assemblyName);
            _logger.Information(
                "Discovered designer {Origin}: {Title} ({Assembly})",
                definition.Origin,
                definition.Title,
                assemblyName);
        }

        var result = found.Values
            .Select(entry => entry.Definition)
            .OrderBy(definition => definition.Origin, StringComparer.Ordinal)
            .ToArray();

        _logger.Information(
            "{Count} designer types discovered across {AssemblyCount} assemblies",
            result.Length,
            scan.AssembliesScanned);
        return result;
    }
}
