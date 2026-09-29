using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// The designer family's slot in module discovery (spec 002, naming convention alignment):
/// empty in the application today, and ready for the first designer module without a change
/// to discovery.
/// </summary>
[Collection(LogCapture.Collection)]
public class DesignerDefinitionDiscoveryTests
{
    [Fact]
    public void Discover_FindsNoDesignerInTheApplication()
    {
        // Arrange: the application's own assemblies, without the test assemblies the walk also
        // finds under a test runner - this one declares a fixture designer below.
        var application = ApplicationAssemblies.Find()
            .Where(assembly => assembly.GetName().Name?.EndsWith(".Tests", StringComparison.Ordinal) != true);

        // Act.
        var result = DesignerDefinitionDiscovery.Discover(application);

        // Assert: no designer module exists yet (src/designers/readme.md).
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_FindsADesignerClassByTheSameScanAsTheOtherFamilies()
    {
        // Act.
        var result = DesignerDefinitionDiscovery.Discover([typeof(DesignerDefinitionDiscoveryTests).Assembly]);

        // Assert.
        var found = Assert.Single(result);
        Assert.Equal("fixture/form", found.Id);
        Assert.Equal("Fixture form", found.Title);
    }

    [Fact]
    public void Discover_RefusesNull()
    {
        // Act & assert.
        Assert.Throws<ArgumentNullException>(() => DesignerDefinitionDiscovery.Discover(null!));
    }
}

/// <summary>What a designer module would declare - the shape discovery looks for.</summary>
internal static class Designer
{
    public static DesignerDefinition[] Definitions { get; } = [new("fixture/form", "Fixture form")];
}
