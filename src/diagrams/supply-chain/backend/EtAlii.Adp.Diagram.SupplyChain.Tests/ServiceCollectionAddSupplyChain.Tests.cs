using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>What the module registers to be created and opened, read back from the registrations.</summary>
public sealed class ServiceCollectionAddSupplyChainTests
{
    [Fact]
    public void TheEmptyDocument_ReadsAsAnEmptyDiagramOfTheCurrentVersion_WithNoProblem()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddSupplyChain();
        using var provider = services.BuildServiceProvider();

        // Act.
        var text = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>())
            .Find(Diagram.SupplyChain.Origin)!
            .CreateEmptyDocument("flows");

        // Assert.
        var model = SupplyChainParser.Parse(LineDocument.Parse(text));
        Assert.Empty(model.Problems);
        Assert.Empty(model.Groups);
        Assert.Empty(model.Nodes);
        Assert.Empty(model.Flows);
        Assert.Equal(SupplyChainModel.CurrentVersion, model.Version);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddSupplyChain();
        services.AddSingleton(TestHistory.For(_ => { }));
        await using var provider = services.BuildServiceProvider();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.SupplyChain.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), "root", SupplyChainExamples.Automotive, null);

        // Assert.
        Assert.IsType<SupplyChainSession>(session);
    }
}
