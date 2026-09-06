using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// Exercises the assembly walk against this test process's real entry assembly and real
/// deployment manifest. The dedicated proof that manifest seeding finds a deployed assembly
/// no metadata reference leads to went with the EtAlii.Adp.DiscoveryProbe project; what
/// still guards that property is indirect - the backend's integration tests discover the
/// diagram modules, which are exactly such assemblies in the host's own deployment.
/// </summary>
[Collection(LogCapture.Collection)]
public class ApplicationAssembliesTests
{
    [Fact]
    public void Find_ReturnsOnlyAssembliesWithTheAdpPrefix()
    {
        // Act.
        var assemblies = ApplicationAssemblies.Find();

        // Assert.
        Assert.NotEmpty(assemblies);
        Assert.All(assemblies, assembly =>
            Assert.StartsWith(ApplicationAssemblies.AssemblyPrefix, assembly.GetName().Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Find_DoesNotReturnTheRunnersOwnAssemblies()
    {
        // Act.
        // The walk starts at the entry assembly and spreads through what is deployed beside
        // it, which under any test runner includes the runner itself. None of that holds
        // diagram types, so none of it belongs in the result.
        var names = ApplicationAssemblies.Find().Select(a => a.GetName().Name).ToList();

        // Assert.
        Assert.DoesNotContain("testhost", names);
        Assert.DoesNotContain(names, name => name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, name => name?.StartsWith("Microsoft.Testing.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Find_ReturnsEachAssemblyOnce()
    {
        // Arrange and act.
        var names = ApplicationAssemblies.Find()
            .Select(assembly => assembly.FullName)
            .ToList();

        // Assert.
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Find_DoesNotWarnOnAHealthyDeployment()
    {
        // Arrange.
        using var logs = LogCapture.Start();

        // Act.
        ApplicationAssemblies.Find();

        // Assert.
        Assert.Empty(logs.Warnings);
    }
}
