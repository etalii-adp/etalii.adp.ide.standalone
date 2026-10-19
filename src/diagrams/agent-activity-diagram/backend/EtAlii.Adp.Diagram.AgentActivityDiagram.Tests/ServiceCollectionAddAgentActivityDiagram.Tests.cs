using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>What the module registers to be created and opened, read back from the registrations.</summary>
public sealed class ServiceCollectionAddAgentActivityDiagramTests
{
    [Fact]
    public void TheEmptyDocument_IsTheHeaderAlone()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddAgentActivityDiagram();
        using var provider = services.BuildServiceProvider();

        // Act.
        var text = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>())
            .Find(Diagram.AgentActivity.Origin)!
            .CreateEmptyDocument("team");

        // Assert.
        Assert.Equal("agent-activity-diagram: 1\r\n", text);
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddAgentActivityDiagram();
        services.AddSingleton(TestHistory.For(_ => { }));
        await using var provider = services.BuildServiceProvider();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.AgentActivity.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), ProjectRoot.From("root"), AadDocumentStoreTests.OneDay, null);

        // Assert.
        Assert.IsType<AadSession>(session);
    }
}
