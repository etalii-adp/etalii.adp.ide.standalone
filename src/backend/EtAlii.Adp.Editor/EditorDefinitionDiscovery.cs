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

    private static readonly ILogger _logger = Log.ForContext(typeof(EditorDefinitionDiscovery));

    private static readonly ToolDefinitionWords Words = new("editor", "Editor id", "Id", "editors",
        "No editors were discovered; files no diagram claims will not open. This usually means the editor modules were not deployed or not referenced");

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
    public static IReadOnlyList<EditorDefinition> Discover(IEnumerable<Assembly> assemblies) =>
        ToolDefinitionDiscovery.Discover<EditorDefinition>(
            assemblies,
            CandidateTypeName,
            Words,
            definitions => definitions.OrderBy(definition => definition.Id, StringComparer.Ordinal),
            _logger);
}
