using System.Reflection;
using Serilog;

namespace EtAlii.Adp;

/// <summary>
/// Finds the designer types the application carries, by the same scan the diagram and editor
/// families run: in each of the application's own assemblies, a static class named
/// <c>Designer</c> with a public static <c>Definitions</c> property holding
/// <see cref="DesignerDefinition"/>s.
/// </summary>
/// <remarks>
/// The designer family's slot in module discovery, empty until the first designer module
/// arrives (see the readme in src/designers). It is here so that module needs no change to discovery,
/// only a <c>Designer</c> class of its own; what the family then needs beyond the scan -
/// coherence rules, a catalog, sessions - comes with it, as it did for the editor family.
/// </remarks>
public static class DesignerDefinitionDiscovery
{
    private const string CandidateTypeName = "Designer";
    private const string DefinitionsPropertyName = "Definitions";

    private static readonly ILogger _logger = Log.ForContext(typeof(DesignerDefinitionDiscovery));

    /// <summary>Scans the application's own assemblies, found by the shared walk.</summary>
    public static IReadOnlyList<DesignerDefinition> Discover() => Discover(ApplicationAssemblies.Find());

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, one per id (the
    /// ordinal-smaller assembly wins, the other families' rule), ordered by id so the result is
    /// the same on every run.
    /// </summary>
    public static IReadOnlyList<DesignerDefinition> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var scan = ToolDefinitionScan.Scan<DesignerDefinition>(assemblies, CandidateTypeName, DefinitionsPropertyName, "designer", _logger);
        var result = scan.Found
            .GroupBy(hit => hit.Definition.Id, StringComparer.Ordinal)
            .Select(group => group.OrderBy(hit => hit.AssemblyName, StringComparer.Ordinal).First().Definition)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        _logger.Information(
            "{Count} designer types discovered across {AssemblyCount} assemblies",
            result.Length,
            scan.AssembliesScanned);
        return result;
    }
}
