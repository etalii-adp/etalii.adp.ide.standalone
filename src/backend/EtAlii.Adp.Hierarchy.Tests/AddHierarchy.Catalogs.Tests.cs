using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Where the three tool catalogs are registered (code-structure FR-057, T165): each by its own
/// kind's registration, and none by the hierarchy area that reads them - the designer catalog was
/// registered from both, and the diagram catalog from the area alone.
/// </summary>
public class AddHierarchyCatalogsTests
{
    [Fact]
    public void AddHierarchy_RegistersNoToolCatalog()
    {
        // Arrange.
        var services = new ServiceCollection();

        // Act.
        services.AddHierarchy();

        // Assert.
        var catalogs = services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetInterfaces().Append(type).Any(IsToolCatalog))
            .Select(type => type.Name);
        Assert.Empty(catalogs);
    }

    private static bool IsToolCatalog(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IToolDefinitionCatalog<>);
}
