using System.Reflection;
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
    public void AfterStartup_EveryDefinitionEveryDeployedModuleDeclaresIsFound()
    {
        // Arrange.
        // Every EtAlii.Adp.Diagram.* assembly deployed beside the host is a module, and each
        // declares one or more definitions through its own Diagram.Definitions array. Reading
        // those arrays pins this test to the deployment rather than to a number that goes stale
        // as modules are added - and it counts definitions rather than assemblies, which is not
        // the same thing since C4 carries seven notations in one module.
        using var _ = _factory.CreateClient();

        // Act.
        var declared = Directory.GetFiles(AppContext.BaseDirectory, "EtAlii.Adp.Diagram.*.dll")
            .Select(IoPath.GetFileNameWithoutExtension)
            .Where(name => name is not null && !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Select(name => Assembly.Load(name!))
            .Select(assembly => assembly.GetTypes().FirstOrDefault(type => type.Name == "Diagram"))
            .Where(type => type is not null)
            .SelectMany(type => (DiagramDefinition[])type!.GetProperty("Definitions")!.GetValue(null)!)
            .Select(definition => definition.Origin.Key)
            .ToList();

        // Assert.
        Assert.NotEmpty(declared);
        Assert.Equal(
            declared.OrderBy(key => key, StringComparer.Ordinal),
            DiagramDefinition.All.Select(definition => definition.Origin.Key));
    }

    [Fact]
    public void AfterStartup_TheModuleCarryingSeveralNotations_ContributesAllOfThem()
    {
        // Arrange.
        // C4 is the reason discovery reads an array. One module, seven notations - and a
        // regression to a singular read would show up here as one of them rather than all.
        using var _ = _factory.CreateClient();

        // Act.
        var c4 = DiagramDefinition.All
            .Where(definition => definition.Origin.Vendor == "c4")
            .Select(definition => definition.Origin.Key)
            .ToList();

        // Assert.
        Assert.Equal(
            ["c4/code", "c4/component", "c4/container", "c4/context", "c4/deployment", "c4/dynamic", "c4/system-landscape"],
            c4);
    }
}
