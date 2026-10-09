using Xunit;
using Alpha = EtAlii.Adp.Designer.Tests.Fixtures.Ordering.Alpha;
using Duplicate = EtAlii.Adp.Designer.Tests.Fixtures.Duplicate;
using Valid = EtAlii.Adp.Designer.Tests.Fixtures.Valid;
using Zulu = EtAlii.Adp.Designer.Tests.Fixtures.Ordering.Zulu;

namespace EtAlii.Adp.Designer.Tests;

/// <summary>
/// The designer half of the shared discovery scan (knowledge-designer Requirement 10.2): the
/// behaviours the diagram and editor families' discovery tests pin, exercised through
/// <see cref="DesignerDefinitionDiscovery"/> - because a shared implementation is only shared
/// if every consumer holds it to the same standard.
/// </summary>
[Collection(LogCapture.Collection)]
public class DesignerDefinitionDiscoveryTests : IDisposable
{
    private readonly LogCapture _logger = LogCapture.Start();

    public void Dispose() => _logger.Dispose();

    private static FakeAssembly AssemblyWith(string name, params Type[] types) => new(name, types);

    [Fact]
    public void Discover_WithNullAssemblies_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => DesignerDefinitionDiscovery.Discover(null!));
    }

    [Fact]
    public void Discover_FindsAStaticDesignerClassWithAPublicStaticDefinition()
    {
        // Act.
        var result = DesignerDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Designer))]);

        // Assert.
        var definition = Assert.Single(result);
        Assert.Equal("fixture/form", definition.Origin);
        Assert.Equal("Fixture form", definition.Title);
    }

    [Fact]
    public void Discover_LogsEachDiscoveredDesignerWithOriginTitleAndAssembly()
    {
        // Act.
        DesignerDefinitionDiscovery.Discover([AssemblyWith("Fixture.A", typeof(Valid.Designer))]);

        // Assert.
        Assert.Contains("Discovered designer fixture/form: Fixture form (Fixture.A)", _logger.Informations);
    }

    [Fact]
    public void Discover_OrdersByOriginOrdinal_RegardlessOfScanOrder()
    {
        // Act: Zulu's assembly is handed in first.
        var result = DesignerDefinitionDiscovery.Discover([
            AssemblyWith("Fixture.Z", typeof(Zulu.Designer)),
            AssemblyWith("Fixture.A", typeof(Alpha.Designer)),
        ]);

        // Assert.
        Assert.Equal(new[] { "fixture/alpha", "fixture/zulu" }, result.Select(definition => definition.Origin));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Discover_DropsADuplicateOrigin_KeepingTheOrdinalSmallerAssembly(bool smallerFirst)
    {
        // Arrange: both declare origin "fixture/form"; Fixture.A must win in either order.
        var smaller = AssemblyWith("Fixture.A", typeof(Valid.Designer));
        var larger = AssemblyWith("Fixture.B", typeof(Duplicate.Designer));
        FakeAssembly[] assemblies = smallerFirst ? [smaller, larger] : [larger, smaller];

        // Act.
        var result = DesignerDefinitionDiscovery.Discover(assemblies);

        // Assert.
        var definition = Assert.Single(result);
        Assert.Equal("Fixture form", definition.Title);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("declared more than once"));
    }
}
