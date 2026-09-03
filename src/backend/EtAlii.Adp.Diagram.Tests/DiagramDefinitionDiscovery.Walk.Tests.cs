using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// Exercises the assembly walk against this test process's real entry assembly and real
/// deployment manifest. The dedicated proof that manifest seeding finds a deployed assembly
/// no metadata reference leads to went with the EtAlii.Adp.DiscoveryProbe project; what
/// still guards that property is indirect - the backend's integration tests discover the
/// diagram modules, which are exactly such assemblies in the host's own deployment.
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
