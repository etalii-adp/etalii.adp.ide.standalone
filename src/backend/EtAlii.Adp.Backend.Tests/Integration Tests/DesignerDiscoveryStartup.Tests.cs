using EtAlii.Adp.Designer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Proves that the real host registers what designer discovery finds rather than discarding
/// it (knowledge-designer Requirement 10.2): its startup leaves a designer catalog in the
/// container, holding exactly the designer types the deployment carries.
/// </summary>
public class DesignerDiscoveryStartupTests(HostFixture host) : IClassFixture<HostFixture>
{
    [Fact]
    public void AfterStartup_TheDesignerCatalogIsServed_HoldingWhatDiscoveryFinds()
    {
        // Act: building the server is what runs Program.cs, and with it discovery and registration.
        using var _ = host.CreateChannel();

        // Assert: the catalog is in the container, and holds what the same walk finds now.
        var catalog = host.Services.GetRequiredService<IDesignerDefinitionCatalog>();
        var discovered = DesignerDefinitionDiscovery.Discover();
        Assert.Equal(
            discovered.Select(definition => definition.Origin),
            catalog.All.Select(definition => definition.Origin));
    }
}
