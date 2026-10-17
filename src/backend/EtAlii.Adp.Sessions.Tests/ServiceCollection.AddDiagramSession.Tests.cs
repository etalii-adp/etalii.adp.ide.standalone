using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Sessions.Tests;

public class ServiceCollectionAddDiagramSessionTests
{
    private static readonly ToolOrigin Origin = new("etalii", "test");

    [Fact]
    public void ARegisteredSession_IsOpenedThroughItsDelegate_WithTheProviderAndWhatItIsOpenedOn()
    {
        // Arrange.
        var services = new ServiceCollection();
        var registered = new object();
        services.AddSingleton(registered);
        DiagramSessionOpening? opened = null;
        IServiceProvider? given = null;
        services.AddDiagramSession(Origin, (provider, opening) =>
        {
            given = provider;
            opened = opening;
            return null!;
        });
        using var provider = services.BuildServiceProvider();
        var watch = ShortGuid.NewShortGuid();

        // Act.
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Origin);
        factory!.Open(watch, "root", "root/body.test", "root/body.adp");

        // Assert.
        Assert.Equal(new DiagramSessionOpening(watch, "root", "root/body.test", "root/body.adp"), opened);
        Assert.Same(registered, given!.GetRequiredService<object>());
    }
}
