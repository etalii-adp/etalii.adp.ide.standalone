using System.Reflection;
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
/// This is a plain class rather than a static initializer so it can be handed a
/// test-controlled set of assemblies. The host runs it once at startup and stores the
/// result in <see cref="DiagramDefinition.All"/>; nothing else should call it.
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
    public IReadOnlyList<DiagramDefinition> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        // Keyed by origin so a duplicate is detected as it arrives; the value remembers which
        // assembly won so a later collision can be reported against it.
        var found = new Dictionary<DiagramOrigin, (DiagramDefinition Definition, string AssemblyName)>();
        var scanned = 0;

        foreach (var assembly in assemblies)
        {
            scanned++;
            var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unnamed>";

            foreach (var type in EnumerateTypes(assembly, assemblyName))
            {
                if (!IsCandidate(type))
                {
                    continue;
                }

                foreach (var definition in TryReadDefinitions(type, assemblyName))
                {
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

    private IEnumerable<Type> EnumerateTypes(Assembly assembly, string assemblyName)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Some types failed to load but the rest are fine; a Diagram class is almost
            // never among the failures, so read what loaded rather than drop the assembly.
            _logger.Warning(
                "Assembly {Assembly} loaded only partially ({FailedCount} types failed); scanning the types that did load",
                assemblyName,
                exception.LoaderExceptions.Length);
            return exception.Types.Where(type => type is not null)!;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.Warning(
                exception,
                "Skipping assembly {Assembly}: its types could not be enumerated",
                assemblyName);
            return [];
        }
    }

    /// <summary>A <c>static class</c> compiles to abstract + sealed; that plus the name is the whole test.</summary>
    private static bool IsCandidate(Type type) =>
        type is { IsClass: true, IsAbstract: true, IsSealed: true } && type.Name == CandidateTypeName;

    /// <summary>
    /// Every definition a <c>Diagram</c> class declares. A module may declare more than one:
    /// the seven C4 types share one engine and one assembly, and splitting them across seven
    /// projects only to satisfy a singular property was the tail wagging the dog.
    /// </summary>
    /// <returns>
    /// Empty for any malformed class, never null - a module that declares its definitions
    /// wrongly costs its own entries and a warning, never anyone else's.
    /// </returns>
    private IReadOnlyList<DiagramDefinition> TryReadDefinitions(Type type, string assemblyName)
    {
        var property = type.GetProperty(DefinitionsPropertyName, BindingFlags.Public | BindingFlags.Static);
        if (property is null)
        {
            LogMalformed(type, assemblyName, $"it has no public static {DefinitionsPropertyName} property");
            return [];
        }

        // Any read-only sequence will do - an array, an ImmutableArray, a List. The property is
        // declared `DiagramDefinition[]` by convention, but insisting on that exact type would
        // reject a module over a choice that makes no difference to anyone reading the result.
        if (!typeof(IReadOnlyList<DiagramDefinition>).IsAssignableFrom(property.PropertyType))
        {
            LogMalformed(
                type,
                assemblyName,
                $"its {DefinitionsPropertyName} property is a {property.PropertyType.Name}, not a sequence of {nameof(DiagramDefinition)}");
            return [];
        }

        try
        {
            if (property.GetValue(null) is not IReadOnlyList<DiagramDefinition> definitions)
            {
                LogMalformed(type, assemblyName, $"its {DefinitionsPropertyName} property returned null");
                return [];
            }

            // A null *inside* the array is its own mistake, and one bad entry should not cost
            // the module its good ones.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            var declared = definitions.Where(definition => definition is not null).ToArray();
            var nulls = definitions.Count - declared.Length;
            if (nulls > 0)
            {
                LogMalformed(
                    type,
                    assemblyName,
                    $"its {DefinitionsPropertyName} property contains {nulls} null {(nulls == 1 ? "entry" : "entries")}");
            }

            if (declared.Length == 0)
            {
                LogMalformed(type, assemblyName, $"its {DefinitionsPropertyName} property declares nothing");
            }

            // A folder-subject type has no sibling body, so an extension it names points at
            // nothing. Dropped rather than half-believed: routing would read the extension and
            // validation would read the folder, and the type would behave as two different
            // things depending on which question was asked. One bad entry costs the module only
            // that entry, exactly as a null one does (ansible-structure-diagram Requirement 2.1).
            var coherent = new List<DiagramDefinition>(declared.Length);
            foreach (var definition in declared)
            {
                if (definition.HasFolderSubject && definition.HasDocumentSibling)
                {
                    LogMalformed(
                        type,
                        assemblyName,
                        $"{definition.Origin} declares a folder subject and the extension '{definition.Extension}', which cannot both be true");
                    continue;
                }

                coherent.Add(definition);
            }

            return coherent;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // GetValue wraps whatever the getter threw.
            var cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            LogMalformed(type, assemblyName, $"reading its {DefinitionsPropertyName} property threw: {cause.Message}");
            return [];
        }
    }

    /// <summary>
    /// One shape of warning for every way a <c>Diagram</c> class can be wrong, so the log
    /// reads the same whichever check rejected it and the reason stays a property of its own.
    /// </summary>
    private static void LogMalformed(Type type, string assemblyName, string reason) =>
        _logger.Warning(
            "Skipping malformed diagram class {Type} in {Assembly}: {Reason}",
            type.FullName ?? type.Name,
            assemblyName,
            reason);
}
