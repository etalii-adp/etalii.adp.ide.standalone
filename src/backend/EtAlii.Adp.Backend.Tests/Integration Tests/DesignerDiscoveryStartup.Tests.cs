using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Proves that the real host registers what designer discovery finds rather than discarding
/// it (knowledge-designer Requirement 10.2): its startup leaves a designer catalog in the
/// container, holding exactly the designer types the deployment carries.
/// </summary>
public class DesignerDiscoveryStartupTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _appDataRoot;

    public DesignerDiscoveryStartupTests(WebApplicationFactory<Program> factory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_appDataRoot);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // The problem cache lives and dies with this test, not in the real user profile
                // (see DiagramDiscoveryStartupTests for what a host booted without this leaves behind).
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public void AfterStartup_TheDesignerCatalogIsServed_HoldingWhatDiscoveryFinds()
    {
        // Act: building the server is what runs Program.cs, and with it discovery and registration.
        using var _ = _factory.CreateClient();

        // Assert: the catalog is in the container, and holds what the same walk finds now.
        var catalog = _factory.Services.GetRequiredService<IDesignerDefinitionCatalog>();
        var discovered = DesignerDefinitionDiscovery.Discover();
        Assert.Equal(
            discovered.Select(definition => definition.Origin),
            catalog.All.Select(definition => definition.Origin));
    }
}
