using System.Reflection;
using EtAlii.Adp.Common;
using Microsoft.Extensions.DependencyModel;
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
/// test-controlled set of assemblies. The host runs it once at startup and stores the
/// result in all DiagramDefinitions, nothing else should call it.
/// <para>
/// It never throws for a bad assembly or a bad candidate. A module that cannot be loaded or
/// that declares its definition wrongly costs exactly one entry in the result and one
/// warning in the log - never the application's startup.
/// </para>
/// </remarks>
public sealed class DiagramDefinitionDiscovery
{
    /// <summary>Only assemblies whose simple name starts with this are ever inspected.</summary>
    public const string AssemblyPrefix = "EtAlii.Adp";

    private const string CandidateTypeName = "Diagram";
    private const string DefinitionsPropertyName = "Definitions";

    private static readonly ILogger _logger = Log.ForContext<DiagramDefinitionDiscovery>();

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, ordered by
    /// origin so the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<DiagramDefinition> Discover()
    {
        var assemblies = FindApplicationAssemblies();
        return Discover(assemblies);
    }

    /// <summary>
    /// Scans exactly the given assemblies and returns the definitions found, ordered by
    /// origin so the result is the same on every run.
    /// </summary>
    public static IReadOnlyList<DiagramDefinition> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        // The mechanical scan - which assemblies, which class name, which property shape - is
        // shared with the editor family (PluginDefinitionScan, modular-text-editors
        // Requirement 1.3); everything below it is this family's own: the folder-subject
        // coherence rule, duplicate detection by origin, and the ordering.
        var scan = PluginDefinitionScan.Scan<DiagramDefinition>(assemblies, CandidateTypeName, DefinitionsPropertyName, "diagram", _logger);
        var scanned = scan.AssembliesScanned;

        // Keyed by origin so a duplicate is detected as it arrives; the value remembers which
        // assembly won so a later collision can be reported against it.
        var found = new Dictionary<DiagramOrigin, (DiagramDefinition Definition, string AssemblyName)>();

        foreach (var hit in scan.Found)
        {
            var assemblyName = hit.AssemblyName;
            var definition = hit.Definition;

            {
                {
                    // A folder-subject type has no sibling body, so an extension it names points
                    // at nothing. Dropped rather than half-believed: routing would read the
                    // extension and validation would read the folder, and the type would behave
                    // as two different things depending on which question was asked. One bad
                    // entry costs the module only that entry
                    // (ansible-structure-diagram Requirement 2.1).
                    if (definition.HasFolderSubject && definition.HasDocumentSibling)
                    {
                        LogMalformed(
                            hit.DeclaringType,
                            assemblyName,
                            $"{definition.Origin} declares a folder subject and the extension '{definition.Extension}', which cannot both be true");
                        continue;
                    }
                    if (found.TryGetValue(definition.Origin, out var existing))
                    {
                        // Keep the ordinal-smaller assembly name so the winner does not depend on
                        // the order assemblies happened to be handed in.
                        var keepExisting = string.CompareOrdinal(existing.AssemblyName, assemblyName) <= 0;
                        var kept = keepExisting ? existing.AssemblyName : assemblyName;
                        var dropped = keepExisting ? assemblyName : existing.AssemblyName;
                        // Each property is named exactly once. An earlier wording mentioned the
                        // kept assembly twice, and a template that repeats a property leaves the
                        // second occurrence unbound - it renders as the bare property name.
                        _logger.Warning(
                            "Diagram origin {Origin} is declared more than once; dropping {DroppedAssembly} and keeping the one from {KeptAssembly}",
                            definition.Origin.ToString(),
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
                        "Discovered diagram type {Origin}: {Title} ({Assembly})",
                        definition.Origin.ToString(),
                        definition.Title,
                        assemblyName);
                }
            }
        }


        var result = found.Values
            .Select(entry => entry.Definition)
            .OrderBy(definition => definition.Origin.Vendor, StringComparer.Ordinal)
            .ThenBy(definition => definition.Origin.Type, StringComparer.Ordinal)
            .ThenBy(definition => definition.Origin.Subtype, StringComparer.Ordinal)
            .ToArray();

        _logger.Information(
            "{Count} diagram types discovered across {AssemblyCount} assemblies",
            result.Length,
            scanned);
        if (result.Length == 0)
        {
            _logger.Warning(
                "No diagram types were discovered; the Add dialog will be empty. This usually means the diagram modules were not deployed or not referenced");
        }

        return result;
    }

    /// <summary>
    /// The application's own assemblies, found by a breadth-first walk of references that
    /// starts from the entry assembly - seeded from its deployment manifest as well, because
    /// the compiled entry assembly records no reference to a module it never uses a type
    /// from, and the host uses none.
    /// </summary>
    /// <remarks>
    /// Follows the walk in https://www.davidguida.net/how-to-find-all-application-assemblies:
    /// dequeue, mark visited, load each unvisited reference, enqueue. Restricted to the
    /// <see cref="AssemblyPrefix"/> so it never descends into the framework.
    /// </remarks>
    public static IReadOnlyList<Assembly> FindApplicationAssemblies()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is null)
        {
            // Happens under some test hosts; fall back to this library so the walk still
            // starts somewhere sensible rather than returning nothing at all.
            entry = typeof(DiagramDefinitionDiscovery).Assembly;
            _logger.Warning(
                "No entry assembly is available; seeding the assembly walk from {Assembly} instead",
                entry.GetName().Name);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();
        var ordered = new List<Assembly>();

        void Enqueue(Assembly assembly)
        {
            if (!visited.Add(assembly.FullName ?? assembly.GetName().Name ?? string.Empty))
            {
                return;
            }

            queue.Enqueue(assembly);

            // The entry assembly is walked from whatever it is, but only returned - and so
            // only scanned - when it is one of ours: under a test runner it is the test host.
            if (IsApplicationAssembly(assembly.GetName().Name))
            {
                ordered.Add(assembly);
            }
        }

        Enqueue(entry);

        var context = DependencyContext.Default;
        if (context is null)
        {
            _logger.Warning(
                "No deployment manifest is available, so the assembly walk is seeded from {Assembly} alone; " +
                "diagram modules the host never references in code may be missed",
                entry.GetName().Name);
        }
        else
        {
            foreach (var library in context.RuntimeLibraries)
            {
                if (!IsApplicationAssembly(library.Name))
                {
                    continue;
                }

                if (TryLoad(new AssemblyName(library.Name)) is { } seeded)
                {
                    Enqueue(seeded);
                }
            }
        }

        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!IsApplicationAssembly(reference.Name))
                {
                    continue;
                }

                if (visited.Contains(reference.FullName))
                {
                    continue;
                }

                if (TryLoad(reference) is { } loaded)
                {
                    Enqueue(loaded);
                }
            }
        }

        return ordered;
    }

    private static bool IsApplicationAssembly(string? simpleName) =>
        simpleName is not null && simpleName.StartsWith(AssemblyPrefix, StringComparison.Ordinal);

    private static Assembly? TryLoad(AssemblyName name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.Warning(
                exception,
                "Skipping assembly {Assembly}: it could not be loaded",
                name.Name);
            return null;
        }
    }

    /// <summary>
    /// One shape of warning for every way a <c>Diagram</c> class can be wrong, so the log
    /// reads the same whichever check rejected it and the reason stays a property of its own.
    /// </summary>
    private static void LogMalformed(Type type, string assemblyName, string reason) =>
        PluginDefinitionScan.LogMalformed(_logger, "diagram", type, assemblyName, reason);
}
