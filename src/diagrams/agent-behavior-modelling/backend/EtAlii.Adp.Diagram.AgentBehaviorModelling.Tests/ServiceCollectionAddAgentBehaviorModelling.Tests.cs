using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>What the module registers to be created and opened, read back from the registrations.</summary>
public sealed class ServiceCollectionAddAgentBehaviorModellingTests
{
    [Fact]
    public void TheEmptyDocument_IsTitledAfterTheFile_AndReadsAsOneNode_WithNoProblem()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddAgentBehaviorModelling();
        using var provider = services.BuildServiceProvider();

        // Act.
        var text = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>())
            .Find(Diagram.AgentBehaviorModelling.Origin)!
            .CreateEmptyDocument("Release notes writer");

        // Assert.
        var model = AbmBody.Parse(text).Model;
        Assert.StartsWith("# Release notes writer\r\n", text, StringComparison.Ordinal);
        Assert.Empty(model.Problems);
        Assert.Single(model.Nodes);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddAgentBehaviorModelling();
        services.AddSingleton(TestHistory.For(_ => { }));
        await using var provider = services.BuildServiceProvider();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.AgentBehaviorModelling.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), "root", AbmExamples.BodyOf("bug-fixer"), null);

        // Assert.
        Assert.IsType<AbmSession>(session);
    }
}
