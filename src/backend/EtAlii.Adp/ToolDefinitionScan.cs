using System.Reflection;
using Serilog;

namespace EtAlii.Adp;

/// <summary>
/// The definition-agnostic half of module discovery: find, in a set of assemblies, every
/// static class with a given name whose public static definitions property holds a sequence.
/// Extracted from the diagram family's discovery so the editor family extends the same scan
/// rather than duplicating it (modular-text-editors Requirement 1.3), and the designer family's
/// slot uses it too; shared by every kind of tool, hence the name (spec 002, naming convention
/// alignment). Each family keeps its own semantics - duplicate detection, coherence rules,
/// ordering - on top of what this returns.
/// </summary>
/// <remarks>
/// It never throws for a bad assembly or a bad candidate. A module that cannot be loaded or
/// that declares its definitions wrongly costs exactly its own entries and a warning in the
/// log - never the application's startup. Logging goes through the CALLER's logger, so the
/// events keep the source context (and therefore the exact log output) each family always had.
/// </remarks>
internal static class ToolDefinitionScan
{
    internal static ToolDefinitionScanResult<T> Scan<T>(
        IEnumerable<Assembly> assemblies,
        string candidateTypeName,
        string definitionsPropertyName,
        string noun,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var found = new List<ToolDefinitionHit<T>>();
        var scanned = 0;

        foreach (var assembly in assemblies)
        {
            scanned++;
            var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unnamed>";

            foreach (var type in EnumerateTypes(assembly, assemblyName, logger))
            {
                if (!IsCandidate(type, candidateTypeName))
                {
                    continue;
                }

                foreach (var definition in TryReadDefinitions<T>(type, assemblyName, definitionsPropertyName, noun, logger))
                {
                    found.Add(new ToolDefinitionHit<T>(definition, type, assemblyName));
                }
            }
        }

        return new ToolDefinitionScanResult<T>(found, scanned);
    }

    private static IEnumerable<Type> EnumerateTypes(Assembly assembly, string assemblyName, ILogger logger)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Some types failed to load but the rest are fine; a definitions class is almost
            // never among the failures, so read what loaded rather than drop the assembly.
            logger.Warning(
                "Assembly {Assembly} loaded only partially ({FailedCount} types failed); scanning the types that did load",
                assemblyName,
                exception.LoaderExceptions.Length);
            return exception.Types.Where(type => type is not null)!;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.Warning(
                exception,
                "Skipping assembly {Assembly}: its types could not be enumerated",
                assemblyName);
            return [];
        }
    }

    /// <summary>A <c>static class</c> compiles to abstract + sealed; that plus the name is the whole test.</summary>
    private static bool IsCandidate(Type type, string candidateTypeName) =>
        type is { IsClass: true, IsAbstract: true, IsSealed: true } && type.Name == candidateTypeName;

    /// <returns>
    /// Empty for any malformed class, never null - a module that declares its definitions
    /// wrongly costs its own entries and a warning, never anyone else's.
    /// </returns>
    private static IReadOnlyList<T> TryReadDefinitions<T>(
        Type type,
        string assemblyName,
        string definitionsPropertyName,
        string noun,
        ILogger logger)
    {
        var property = type.GetProperty(definitionsPropertyName, BindingFlags.Public | BindingFlags.Static);
        if (property is null)
        {
            LogMalformed(logger, noun, type, assemblyName, $"it has no public static {definitionsPropertyName} property");
            return [];
        }

        // Any read-only sequence will do - an array, an ImmutableArray, a List. The property is
        // declared as an array by convention, but insisting on that exact type would reject a
        // module over a choice that makes no difference to anyone reading the result.
        if (!typeof(IReadOnlyList<T>).IsAssignableFrom(property.PropertyType))
        {
            LogMalformed(
                logger,
                noun,
                type,
                assemblyName,
                $"its {definitionsPropertyName} property is a {property.PropertyType.Name}, not a sequence of {typeof(T).Name}");
            return [];
        }

        try
        {
            if (property.GetValue(null) is not IReadOnlyList<T> definitions)
            {
                LogMalformed(logger, noun, type, assemblyName, $"its {definitionsPropertyName} property returned null");
                return [];
            }

            // A null *inside* the array is its own mistake, and one bad entry should not cost
            // the module its good ones.
            var declared = definitions.Where(definition => definition is not null).ToArray();
            var nulls = definitions.Count - declared.Length;
            if (nulls > 0)
            {
                LogMalformed(
                    logger,
                    noun,
                    type,
                    assemblyName,
                    $"its {definitionsPropertyName} property contains {nulls} null {(nulls == 1 ? "entry" : "entries")}");
            }

            if (declared.Length == 0)
            {
                LogMalformed(logger, noun, type, assemblyName, $"its {definitionsPropertyName} property declares nothing");
            }

            return declared;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // GetValue wraps whatever the getter threw.
            var cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            LogMalformed(logger, noun, type, assemblyName, $"reading its {definitionsPropertyName} property threw: {cause.Message}");
            return [];
        }
    }

    /// <summary>
    /// One shape of warning for every way a definitions class can be wrong, so the log reads
    /// the same whichever check rejected it. The noun keeps each family's rendered text
    /// exactly what it was before the extraction.
    /// </summary>
    internal static void LogMalformed(ILogger logger, string noun, Type type, string assemblyName, string reason) =>
        logger.Warning(
            "Skipping malformed {Noun} class {Type} in {Assembly}: {Reason}",
            noun,
            type.FullName ?? type.Name,
            assemblyName,
            reason);
}
