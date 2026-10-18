using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>What the module registers to be created and opened, read back from the registrations.</summary>
public sealed class ServiceCollectionAddGartnerHypeCycleGraphTests
{
    [Fact]
    public void TheEmptyDocument_ReadsAsAnEmptyDiagramOfTheCurrentVersion_WithNoProblem()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddGartnerHypeCycleGraph();
        using var provider = services.BuildServiceProvider();

        // Act.
        var text = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>())
            .Find(Diagram.HypeCycleGraph.Origin)!
            .CreateEmptyDocument("flows");

        // Assert.
        var model = GhgParser.Parse(GhgBody.Parse(text));
        Assert.Empty(model.Problems);
        Assert.Empty(model.Trends);
        Assert.Empty(model.Influences);
        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddGartnerHypeCycleGraph();
        services.AddSingleton(TestHistory.For(_ => { }));
        await using var provider = services.BuildServiceProvider();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.HypeCycleGraph.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), "root", GhgModuleFiles.Example, null);

        // Assert.
        Assert.IsType<GhgSession>(session);
    }
}
