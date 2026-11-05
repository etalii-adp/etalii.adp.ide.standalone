using EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>What the module registers to be created and opened, read back from the registrations.</summary>
public sealed class ServiceCollectionAddFunctionalDecompositionGraphTests
{
    [Fact]
    public void TheEmptyDocument_ReadsAsAnEmptyDiagramOfTheCurrentVersion_WithNoProblem()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddFunctionalDecompositionGraph();
        using var provider = services.BuildServiceProvider();

        // Act.
        var text = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>())
            .Find(Diagram.FunctionalDecompositionGraph.Origin)!
            .CreateEmptyDocument("flows");

        // Assert.
        var model = FdgParser.Parse(LineDocument.Parse(text));
        Assert.Empty(model.Problems);
        Assert.Empty(model.Elements);
        Assert.Empty(model.Connections);
        Assert.Equal(FdgModel.CurrentVersion, model.Version);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddSingleton<IFileWriter>(new FileWriter());
        services.AddFunctionalDecompositionGraph();
        services.AddSingleton(TestHistory.For(_ => { }));
        await using var provider = services.BuildServiceProvider();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.FunctionalDecompositionGraph.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), ProjectRoot.From("root"), FieldServiceExample.Path, null);

        // Assert.
        Assert.IsType<FdgSession>(session);
    }
}
