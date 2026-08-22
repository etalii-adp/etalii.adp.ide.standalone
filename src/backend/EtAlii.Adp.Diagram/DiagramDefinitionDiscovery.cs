using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Finds every diagram type the application carries by looking, in each of its own
/// assemblies, for a static class named <c>Diagram</c> with a public static
/// <c>Definition</c> property of type <see cref="DiagramDefinition"/> - the shape every
/// diagram-type module already exposes.
/// </summary>
/// <remarks>
/// This is a plain class rather than a static initializer so it can be handed a logger and
/// a test-controlled set of assemblies. The host runs it once at startup and stores the
/// result in <see cref="DiagramDefinition.All"/>; nothing else should call it.
/// <para>
/// It never throws for a bad assembly or a bad candidate. A module that cannot be loaded or
/// that declares its definition wrongly costs exactly one entry in the result and one
/// warning in the log - never the application's startup.
/// </para>
/// </remarks>
public sealed partial class DiagramDefinitionDiscovery
{
    /// <summary>Only assemblies whose simple name starts with this are ever inspected.</summary>
    public const string AssemblyPrefix = "EtAlii.Adp";

    private const string CandidateTypeName = "Diagram";
    private const string DefinitionPropertyName = "Definition";

    private readonly ILogger<DiagramDefinitionDiscovery> _logger;

    public DiagramDefinitionDiscovery(ILogger<DiagramDefinitionDiscovery> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

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

                if (TryReadDefinition(type, assemblyName) is not { } definition)
                {
                    continue;
                }

                if (found.TryGetValue(definition.Origin, out var existing))
                {
                    // Keep the ordinal-smaller assembly name so the winner does not depend on
                    // the order assemblies happened to be handed in.
                    var keepExisting = string.CompareOrdinal(existing.AssemblyName, assemblyName) <= 0;
                    LogOriginCollision(
                        definition.Origin.ToString(),
                        keepExisting ? existing.AssemblyName : assemblyName,
                        keepExisting ? assemblyName : existing.AssemblyName);

                    if (!keepExisting)
                    {
                        found[definition.Origin] = (definition, assemblyName);
                    }

                    continue;
                }

                found[definition.Origin] = (definition, assemblyName);
                LogDiscovered(definition.Origin.ToString(), definition.Title, assemblyName);
            }
        }

        var result = found.Values
            .Select(entry => entry.Definition)
            .OrderBy(definition => definition.Origin.Vendor, StringComparer.Ordinal)
            .ThenBy(definition => definition.Origin.Type, StringComparer.Ordinal)
            .ThenBy(definition => definition.Origin.Subtype, StringComparer.Ordinal)
            .ToArray();

        LogSummary(result.Length, scanned);
        if (result.Length == 0)
        {
            LogNoneDiscovered();
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
    public static IReadOnlyList<Assembly> FindApplicationAssemblies(ILogger? logger = null)
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is null)
        {
            // Happens under some test hosts; fall back to this library so the walk still
            // starts somewhere sensible rather than returning nothing at all.
            entry = typeof(DiagramDefinitionDiscovery).Assembly;
            logger?.LogWarning(
                "No entry assembly is available; seeding the assembly walk from {Assembly} instead",
                entry.GetName().Name);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();
        var ordered = new List<Assembly>();

        void Enqueue(Assembly assembly)
        {
            if (visited.Add(assembly.FullName ?? assembly.GetName().Name ?? string.Empty))
            {
                queue.Enqueue(assembly);
                ordered.Add(assembly);
            }
        }

        Enqueue(entry);

        var context = DependencyContext.Default;
        if (context is null)
        {
            logger?.LogWarning(
                "No deployment manifest is available, so the assembly walk is seeded from {Assembly} alone; " +
                "diagram modules the host never references in code may be missed",
                entry.GetName().Name);
        }
        else
        {
            foreach (var library in context.RuntimeLibraries)
            {
                if (!library.Name.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryLoad(new AssemblyName(library.Name), logger) is { } seeded)
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
                if (reference.Name is null || !reference.Name.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (visited.Contains(reference.FullName))
                {
                    continue;
                }

                if (TryLoad(reference, logger) is { } loaded)
                {
                    Enqueue(loaded);
                }
            }
        }

        return ordered;
    }

    private static Assembly? TryLoad(AssemblyName name, ILogger? logger)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger?.LogWarning(
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
            LogPartialTypeLoad(assemblyName, exception.LoaderExceptions.Length);
            return exception.Types.Where(type => type is not null)!;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogAssemblySkipped(exception, assemblyName);
            return [];
        }
    }

    /// <summary>A <c>static class</c> compiles to abstract + sealed; that plus the name is the whole test.</summary>
    private static bool IsCandidate(Type type) =>
        type is { IsClass: true, IsAbstract: true, IsSealed: true } && type.Name == CandidateTypeName;

    private DiagramDefinition? TryReadDefinition(Type type, string assemblyName)
    {
        var property = type.GetProperty(DefinitionPropertyName, BindingFlags.Public | BindingFlags.Static);
        if (property is null)
        {
            LogMalformed(type.FullName ?? type.Name, assemblyName, "it has no public static Definition property");
            return null;
        }

        if (!typeof(DiagramDefinition).IsAssignableFrom(property.PropertyType))
        {
            LogMalformed(
                type.FullName ?? type.Name,
                assemblyName,
                $"its Definition property is a {property.PropertyType.Name}, not a {nameof(DiagramDefinition)}");
            return null;
        }

        try
        {
            if (property.GetValue(null) is DiagramDefinition definition)
            {
                return definition;
            }

            LogMalformed(type.FullName ?? type.Name, assemblyName, "its Definition property returned null");
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // GetValue wraps whatever the getter threw.
            var cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            LogMalformed(type.FullName ?? type.Name, assemblyName, $"reading its Definition property threw: {cause.Message}");
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered diagram type {Origin}: {Title} ({Assembly})")]
    private partial void LogDiscovered(string origin, string title, string assembly);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} diagram types discovered across {AssemblyCount} assemblies")]
    private partial void LogSummary(int count, int assemblyCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No diagram types were discovered; the Add dialog will be empty. This usually means the diagram modules were not deployed or not referenced")]
    private partial void LogNoneDiscovered();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping assembly {Assembly}: its types could not be enumerated")]
    private partial void LogAssemblySkipped(Exception exception, string assembly);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assembly {Assembly} loaded only partially ({FailedCount} types failed); scanning the types that did load")]
    private partial void LogPartialTypeLoad(string assembly, int failedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping malformed diagram class {Type} in {Assembly}: {Reason}")]
    private partial void LogMalformed(string type, string assembly, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagram origin {Origin} is declared by both {KeptAssembly} and {DroppedAssembly}; keeping the one from {KeptAssembly}")]
    private partial void LogOriginCollision(string origin, string keptAssembly, string droppedAssembly);
}
