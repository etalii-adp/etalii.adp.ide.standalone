using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EtAlii.Adp.Designer.Tests;

/// <summary>
/// What registering the discovered designers gives the host (knowledge-designer
/// Requirement 10.2): the definitions and their catalog as services, and each module's own
/// registrations run.
/// </summary>
public class AddDesignerDefinitionsTests
{
    [Fact]
    public void AddDesignerDefinitions_WithNullDefinitions_Throws()
    {
        // Arrange.
        var builder = Host.CreateEmptyApplicationBuilder(null);

        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => builder.AddDesignerDefinitions(null!));
    }

    [Fact]
    public void AddDesignerDefinitions_RegistersTheCatalogHoldingExactlyTheDefinitions()
    {
        // Arrange.
        var builder = Host.CreateEmptyApplicationBuilder(null);
        DesignerDefinition[] definitions = [new("fixture/form", "Fixture form"), new("fixture/sheet", "Fixture sheet")];

        // Act.
        builder.AddDesignerDefinitions(definitions);
        using var host = builder.Build();

        // Assert.
        var catalog = host.Services.GetRequiredService<IDesignerDefinitionCatalog>();
        Assert.Equal(new[] { "fixture/form", "fixture/sheet" }, catalog.All.Select(definition => definition.Origin));
        Assert.Same(definitions, host.Services.GetRequiredService<IReadOnlyList<DesignerDefinition>>());
    }

    [Fact]
    public void AddDesignerDefinitions_RunsEachModulesOwnRegistrations()
    {
        // Arrange: each definition registers a marker naming itself.
        var builder = Host.CreateEmptyApplicationBuilder(null);
        DesignerDefinition[] definitions =
        [
            new("fixture/form", "Fixture form", Build: host => host.Services.AddSingleton(new Marker("form"))),
            new("fixture/sheet", "Fixture sheet", Build: host => host.Services.AddSingleton(new Marker("sheet"))),
            new("fixture/plain", "Fixture without registrations"),
        ];

        // Act.
        builder.AddDesignerDefinitions(definitions);
        using var host = builder.Build();

        // Assert.
        Assert.Equal(new[] { "form", "sheet" }, host.Services.GetServices<Marker>().Select(marker => marker.Name));
    }

    [Fact]
    public void AddDesignerDefinitions_WithNoDesigner_StillRegistersAnEmptyCatalog()
    {
        // Arrange.
        var builder = Host.CreateEmptyApplicationBuilder(null);

        // Act.
        builder.AddDesignerDefinitions([]);
        using var host = builder.Build();

        // Assert: code that asks for the catalog is served in an application without designers.
        Assert.Empty(host.Services.GetRequiredService<IDesignerDefinitionCatalog>().All);
    }

    private sealed record Marker(string Name);
}
