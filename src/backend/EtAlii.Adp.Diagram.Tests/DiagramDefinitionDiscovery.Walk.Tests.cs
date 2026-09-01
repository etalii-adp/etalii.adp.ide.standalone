using System.Reflection;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// Exercises the assembly walk against this test process's real entry assembly and real
/// deployment manifest - the one place the manifest seeding can be proven rather than mocked.
/// </summary>
[Collection(LogCapture.Collection)]
public class DiagramDefinitionDiscoveryWalkTests
{
    [Fact]
    public void FindApplicationAssemblies_ReturnsOnlyAssembliesWithTheAdpPrefix()
    {
        // Act.
        var assemblies = DiagramDefinitionDiscovery.FindApplicationAssemblies();

        // Assert.
        Assert.NotEmpty(assemblies);
        Assert.All(assemblies, assembly =>
            Assert.StartsWith(DiagramDefinitionDiscovery.AssemblyPrefix, assembly.GetName().Name, StringComparison.Ordinal));
    }

    [Fact]
    public void FindApplicationAssemblies_ReachesAssembliesTheEntryAssemblyNeverReferences()
    {
        // Arrange.
        // The walk returns assemblies no chain of metadata references from the entry assembly
        // leads to - exactly the situation the real host is in with the diagram modules
        // (Requirement 3.3). Finding them therefore proves the walk is seeded from the
        // deployment manifest; the unseeded walk from the article never leaves the closure
        // computed below.
        //
        // Which assembly is the entry one depends on the runner - under xUnit v3 the test
        // project is its own executable, under a VSTest host it was that host - so the test
        // compares against the closure rather than against one particular entry assembly, and
        // proves the same property either way.
        // The probe assembly is the demonstration subject: deployed by this test project with
        // ReferenceOutputAssembly=false, so no metadata chain can ever lead to it. EtAlii.Adp
        // used to play this role, until the shared PluginDefinitionScan gave the Diagram
        // library a real metadata reference to it - the flip the comment above always
        // anticipated.
        var reachableFromEntry = ReachableByMetadataFromEntry();
        Assert.DoesNotContain("EtAlii.Adp.DiscoveryProbe", reachableFromEntry);

        // Act.
        var names = DiagramDefinitionDiscovery.FindApplicationAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.Contains("EtAlii.Adp.Diagram.Tests", names);
        Assert.Contains("EtAlii.Adp.Diagram", names);
        Assert.Contains("EtAlii.Adp.DiscoveryProbe", names);
    }

    /// <summary>
    /// Every EtAlii.Adp.* assembly a plain metadata walk outwards from the entry assembly can
    /// reach - that is, everything discovery would find with no manifest seeding at all.
    /// </summary>
    private static HashSet<string> ReachableByMetadataFromEntry()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        if (Assembly.GetEntryAssembly() is not { } entry)
        {
            return reached;
        }

        reached.Add(entry.GetName().Name!);
        var pending = new Queue<Assembly>();
        pending.Enqueue(entry);
        while (pending.Count > 0)
        {
            foreach (var reference in pending.Dequeue().GetReferencedAssemblies())
            {
                if (reference.Name is not { } name ||
                    !name.StartsWith(DiagramDefinitionDiscovery.AssemblyPrefix, StringComparison.Ordinal) ||
                    !reached.Add(name))
                {
                    continue;
                }

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception)
                {
                    // A reference that will not load reaches nothing further. It stays counted
                    // as reachable, which only makes the assertion above stricter.
                }
            }
        }

        return reached;
    }

    [Fact]
    public void FindApplicationAssemblies_FindsAssembliesNoMetadataReferenceLeadsTo()
    {
        // Arrange.
        // The probe is deployed (ReferenceOutputAssembly=false) yet no assembly in the walk
        // carries a metadata reference to it - the very pruning Requirement 3.3 measured on
        // the host. The walk still returns it, which it can only have got from the manifest.
        // (EtAlii.Adp itself held this role until PluginDefinitionScan made the Diagram
        // library's reference to it real - the flip the original soft assertion anticipated.)
        var assemblies = DiagramDefinitionDiscovery.FindApplicationAssemblies();

        // Act.
        var anythingReferencesTheProbe = assemblies.Any(assembly =>
            assembly.GetReferencedAssemblies().Any(reference => reference.Name == "EtAlii.Adp.DiscoveryProbe"));

        // Assert.
        Assert.Contains(assemblies, a => a.GetName().Name == "EtAlii.Adp.DiscoveryProbe");
        Assert.False(
            anythingReferencesTheProbe,
            "Something now carries a metadata reference to EtAlii.Adp.DiscoveryProbe; deployed-but-unreferenced is its entire role");
    }

    [Fact]
    public void FindApplicationAssemblies_DoesNotReturnTheRunnersOwnAssemblies()
    {
        // Act.
        // The walk starts at the entry assembly and spreads through what is deployed beside
        // it, which under any test runner includes the runner itself. None of that holds
        // diagram types, so none of it belongs in the result.
        var names = DiagramDefinitionDiscovery.FindApplicationAssemblies().Select(a => a.GetName().Name).ToList();

        // Assert.
        Assert.DoesNotContain("testhost", names);
        Assert.DoesNotContain(names, name => name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, name => name?.StartsWith("Microsoft.Testing.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FindApplicationAssemblies_ReturnsEachAssemblyOnce()
    {
        // Arrange and act.
        var names = DiagramDefinitionDiscovery.FindApplicationAssemblies()
            .Select(assembly => assembly.FullName)
            .ToList();

        // Assert.
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void FindApplicationAssemblies_DoesNotWarnOnAHealthyDeployment()
    {
        // Arrange.
        using var logs = LogCapture.Start();

        // Act.
        DiagramDefinitionDiscovery.FindApplicationAssemblies();

        // Assert.
        Assert.Empty(logs.Warnings);
    }

    [Fact]
    public void DiscoverOverTheWalk_FindsThisAssemblysFixtures()
    {
        // Arrange.
        // The end-to-end shape the host uses: walk, then discover. The fixtures live here, so
        // a walk that really reaches this assembly yields them.

        // Act.
        var result = DiagramDefinitionDiscovery.Discover();

        // Assert.
        Assert.Contains(result, d => d.Origin.Key == "fixture/valid");
        Assert.Contains(result, d => d.Origin.Key == "alpha/z");
    }
}
