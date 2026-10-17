using System.Reflection;
using Serilog;

namespace EtAlii.Adp;

/// <summary>A tool type's definition, as every kind declares one: what discovery keys it by and what it calls it.</summary>
public interface IToolDefinition
{
    /// <summary>
    /// What makes the definition one of a kind, as the log writes it: a diagram's origin key, a
    /// designer's origin, an editor's id. Two definitions with the same key are one declared twice.
    /// </summary>
    string Key { get; }

    /// <summary>The name a user reads.</summary>
    string Title { get; }
}

/// <summary>How a tool kind's discovery speaks of what it finds, word for word as each kind always has.</summary>
/// <param name="Kind">The noun for one definition: <c>diagram type</c>, <c>editor</c>, <c>designer</c>.</param>
/// <param name="KeyLabel">What a key is called at the start of a sentence: <c>Diagram origin</c>, <c>Editor id</c>.</param>
/// <param name="KeyProperty">The log property the key is written to: <c>Origin</c> or <c>Id</c>.</param>
/// <param name="Plural">The noun for the count: <c>diagram types</c>, <c>editors</c>, <c>designer types</c>.</param>
/// <param name="NoneFound">The warning when none is found, or null for a kind whose absence is no fault.</param>
public sealed record ToolDefinitionWords(string Kind, string KeyLabel, string KeyProperty, string Plural, string? NoneFound = null);

/// <summary>
/// The one discovery of the three tool kinds (code-structure FR-057): the scan, then each
/// definition kept once by its key, the duplicate dropped with a warning that names both assemblies,
/// the found ones logged and counted. A kind supplies its words, its order and, where it has one, the
/// rule that makes a definition malformed.
/// </summary>
public static class ToolDefinitionDiscovery
{
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <param name="candidateTypeName">The static class a module declares its definitions in.</param>
    /// <param name="words">How this kind's log speaks.</param>
    /// <param name="order">The order the definitions are handed out in.</param>
    /// <param name="logger">The kind's own logger, so its lines carry its source.</param>
    /// <param name="malformed">Why a definition cannot be believed, or null when it can; null for a kind with no such rule.</param>
    /// <typeparam name="T">The kind's definition.</typeparam>
    public static IReadOnlyList<T> Discover<T>(
        IEnumerable<Assembly> assemblies,
        string candidateTypeName,
        ToolDefinitionWords words,
        Func<IEnumerable<T>, IEnumerable<T>> order,
        ILogger logger,
        Func<T, string?>? malformed = null)
        where T : IToolDefinition
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(logger);

        var scan = ToolDefinitionScan.Scan<T>(assemblies, candidateTypeName, "Definitions", words.Kind.Split(' ')[0], logger);

        // Keyed by key so a duplicate is detected as it arrives; the value remembers which
        // assembly won so a later collision can be reported against it.
        var found = new Dictionary<string, (T Definition, string AssemblyName)>(StringComparer.Ordinal);
        var duplicate = $"{words.KeyLabel} {{{words.KeyProperty}}} is declared more than once; dropping {{DroppedAssembly}} and keeping the one from {{KeptAssembly}}";
        var discovered = $"Discovered {words.Kind} {{{words.KeyProperty}}}: {{Title}} ({{Assembly}})";

        foreach (var hit in scan.Found)
        {
            var definition = hit.Definition;
            var assemblyName = hit.AssemblyName;

            if (malformed?.Invoke(definition) is { } reason)
            {
                // Dropped rather than half-believed; one bad entry costs the module only that entry.
                ToolDefinitionScan.LogMalformed(logger, words.Kind.Split(' ')[0], hit.DeclaringType, assemblyName, reason);
                continue;
            }

            if (found.TryGetValue(definition.Key, out var existing))
            {
                // Keep the ordinal-smaller assembly name so the winner does not depend on the
                // order assemblies happened to be handed in.
                var keepExisting = string.CompareOrdinal(existing.AssemblyName, assemblyName) <= 0;
                var kept = keepExisting ? existing.AssemblyName : assemblyName;
                var dropped = keepExisting ? assemblyName : existing.AssemblyName;
                logger.Warning(duplicate, definition.Key, dropped, kept);

                if (!keepExisting)
                {
                    found[definition.Key] = (definition, assemblyName);
                }

                continue;
            }

            found[definition.Key] = (definition, assemblyName);
            logger.Information(discovered, definition.Key, definition.Title, assemblyName);
        }

        var result = order(found.Values.Select(entry => entry.Definition)).ToArray();

        logger.Information($"{{Count}} {words.Plural} discovered across {{AssemblyCount}} assemblies", result.Length, scan.AssembliesScanned);
        if (result.Length == 0 && words.NoneFound is { } noneFound)
        {
            logger.Warning(noneFound);
        }

        return result;
    }
}
