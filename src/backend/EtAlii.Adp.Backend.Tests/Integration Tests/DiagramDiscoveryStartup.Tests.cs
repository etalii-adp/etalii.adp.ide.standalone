using EtAlii.Adp.Diagram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using IoPath = System.IO.Path;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Proves discovery against the real host and the real deployment: the host's startup runs
/// the seeded assembly walk, and the diagram modules it carries - which its compiled metadata
/// never references - end up in <see cref="DiagramDefinition.All"/>. This is the test that
/// would have failed with an unseeded walk, which returns nothing here.
/// </summary>
public class DiagramDiscoveryStartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DiagramDiscoveryStartupTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("developer"));
    }

    [Fact]
    public void AfterStartup_AllHoldsTheDeployedDiagramTypes()
    {
        // Act.
        // Building the server is what runs Program.cs, and with it discovery. The cache is
        // per process and other test classes build hosts too, so this reads it rather than
        // calling Initialize - whichever host ran first filled it from the same deployment.
        using var _ = _factory.CreateClient();

        // Assert.
        Assert.NotEmpty(DiagramDefinition.All);
        // A scaffolded module known to declare a Definition, found by origin not by type -
        // this test must not reference any module.
        Assert.Contains(DiagramDefinition.All, definition => definition.Origin.Key == "c4/context");
    }

    [Fact]
    public void AfterStartup_AllIsOrderedByOriginAndFreeOfDuplicates()
    {
        // Arrange.
        using var _ = _factory.CreateClient();

        // Act.
        var keys = DiagramDefinition.All.Select(definition => definition.Origin.Key).ToList();

        // Assert.
        Assert.Equal(keys.OrderBy(key => key, StringComparer.Ordinal), keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AfterStartup_EveryDeployedModuleWithADefinitionIsFound()
    {
        // Arrange.
        // Every EtAlii.Adp.Diagram.* assembly deployed beside the host is a module. Each one
        // that declares a Definition must be in All; this pins the count to the deployment
        // rather than to a number that goes stale as modules are added.
        using var _ = _factory.CreateClient();

        // Act.
        var deployedModules = Directory.GetFiles(AppContext.BaseDirectory, "EtAlii.Adp.Diagram.*.dll")
            .Select(IoPath.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .ToList();

        // Assert.
        Assert.NotEmpty(deployedModules);
        Assert.Equal(deployedModules.Count, DiagramDefinition.All.Count);
    }
}
