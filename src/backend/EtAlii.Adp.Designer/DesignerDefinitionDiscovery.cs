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

    private static readonly ILogger _logger = Log.ForContext(typeof(DesignerDefinitionDiscovery));

    private static readonly ToolDefinitionWords Words = new("designer", "Designer origin", "Origin", "designer types");

    /// <summary>Scans the application's own assemblies, found by the shared walk.</summary>
    public static IReadOnlyList<DesignerDefinition> Discover() => Discover(ApplicationAssemblies.Find());

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, one per origin,
    /// ordered by origin so the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<DesignerDefinition> Discover(IEnumerable<Assembly> assemblies) =>
        ToolDefinitionDiscovery.Discover<DesignerDefinition>(
            assemblies,
            CandidateTypeName,
            Words,
            definitions => definitions.OrderBy(definition => definition.Origin, StringComparer.Ordinal),
            _logger);
}
