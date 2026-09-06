using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using Serilog;

namespace EtAlii.Adp;

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
/// <para>
/// Lives beside <see cref="PluginDefinitionScan"/> because the two are halves of the same
/// discovery infrastructure - this answers <em>which assemblies</em>, the scan answers
/// <em>what is in them</em> - and both the diagram and editor definition discoveries walk
/// through here (backend-project-decomposition task 8: the walk left
/// <c>DiagramDefinitionDiscovery</c> so <c>EtAlii.Adp.Editor</c> no longer needs a
/// <c>Diagram</c> reference, which would otherwise cycle with Diagram's new Editor one).
/// </para>
/// </remarks>
public static class ApplicationAssemblies
{
    public const string AssemblyPrefix = "EtAlii.Adp";

    private static readonly ILogger _logger = Log.ForContext(typeof(ApplicationAssemblies));

    public static IReadOnlyList<Assembly> Find()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is null)
        {
            // Happens under some test hosts; fall back to this library so the walk still
            // starts somewhere sensible rather than returning nothing at all.
            entry = typeof(ApplicationAssemblies).Assembly;
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
}
