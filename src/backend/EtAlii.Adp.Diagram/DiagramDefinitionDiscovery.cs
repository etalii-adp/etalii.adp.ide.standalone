using System.Reflection;
using Serilog;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Finds every diagram type the application carries by looking, in each of its own
/// assemblies, for a static class named <c>Diagram</c> with a public static
/// <c>Definitions</c> property holding <see cref="DiagramDefinition"/>s - the shape every
/// diagram-type module already exposes.
/// </summary>
/// <remarks>
/// This is a static class without state  so it can be handed a
/// test-controlled set of assemblies. The host runs it once at startup and registers the
/// result in its container; nothing else should call it.
/// <para>
/// It never throws for a bad assembly or a bad candidate. A module that cannot be loaded or
/// that declares its definition wrongly costs exactly one entry in the result and one
/// warning in the log - never the application's startup.
/// </para>
/// </remarks>
public static class DiagramDefinitionDiscovery
{
    private const string CandidateTypeName = "Diagram";

    private static readonly ILogger _logger = Log.ForContext(typeof(DiagramDefinitionDiscovery));

    private static readonly ToolDefinitionWords Words = new("diagram type", "Diagram origin", "Origin", "diagram types",
        "No diagram types were discovered; the Add dialog will be empty. This usually means the diagram modules were not deployed or not referenced");

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, ordered by
    /// origin so the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<DiagramDefinition> Discover()
    {
        var assemblies = ApplicationAssemblies.Find();
        return Discover(assemblies);
    }

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, ordered by
    /// origin so the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<DiagramDefinition> Discover(IEnumerable<Assembly> assemblies) =>
        ToolDefinitionDiscovery.Discover<DiagramDefinition>(
            assemblies,
            CandidateTypeName,
            Words,
            definitions => definitions
                .OrderBy(definition => definition.Origin.Vendor, StringComparer.Ordinal)
                .ThenBy(definition => definition.Origin.Type, StringComparer.Ordinal)
                .ThenBy(definition => definition.Origin.Subtype, StringComparer.Ordinal),
            _logger,
            // A folder-subject type has no sibling body, so an extension it names points at nothing.
            // Dropped rather than half-believed: routing would read the extension and validation
            // would read the folder, and the type would behave as two different things depending
            // on which question was asked (ansible-structure-diagram Requirement 2.1).
            definition => definition is { HasFolderSubject: true, HasDocumentSibling: true }
                ? $"{definition.Origin} declares a folder subject and the extension '{definition.Extension}', which cannot both be true"
                : null);
}
