using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The end-to-end shape the host uses - walk, then discover - kept beside the
/// discovery it exercises; the walk's own tests moved to EtAlii.Adp.Tests with
/// <c>ApplicationAssemblies</c> (backend-project-decomposition task 8).
/// </summary>
public class DiagramDefinitionDiscoveryWalkTests
{
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
