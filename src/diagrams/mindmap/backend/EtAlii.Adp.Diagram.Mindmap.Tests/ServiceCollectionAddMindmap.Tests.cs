using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The module's one registration point: every seam resolves, and the session and the empty map it
/// registers are what the host opens and writes.
/// </summary>
public class ServiceCollectionAddMindmapTests
{
    /// <summary>The seams a diagram module plugs into, and what this one is expected to fill.</summary>
    public static TheoryData<Type> RegisteredSeams() =>
    [
        typeof(IDiagramDocumentFactory),
        typeof(IDiagramSessionFactory),
        typeof(IDiagramDocumentReloader),
        typeof(IContextSourceResolver),
        typeof(IContextActionProvider),
        typeof(IContextPropertyProvider),
        typeof(IDiagramToolboxProvider),
        typeof(IMindmapDocumentStore),
    ];

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection()
            .AddSingleton<IDiagramDefinitionCatalog>(new MindmapTestProjectCatalog([Diagram.Mindmap]))
            .AddSingleton<DiagramFileRouter>()
            .AddCommands().AddHierarchyCommandHandlers();

        services.AddMindmap(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Theory]
    [MemberData(nameof(RegisteredSeams))]
    public void OneCall_FillsEverySeamTheModuleUses(Type seam)
    {
        // Arrange & act.
        using var provider = Build();

        // Assert.
        Assert.NotNull(provider.GetService(seam));
    }

    [Fact]
    public async Task ASession_IsOpenedOnTheBody()
    {
        // Arrange.
        await using var provider = Build();
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Diagram.Mindmap.Origin)!;

        // Act.
        await using var session = factory.Open(ShortGuid.NewShortGuid(), ProjectRoot.From("root"), IoPath.Combine("Fixtures", "architecture.mm"), null);

        // Assert.
        Assert.IsType<MindmapSession>(session);
        Assert.NotEmpty(await session.BaselineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheEmptyDocument_IsOneRootNamedAfterTheFile()
    {
        // Arrange.
        using var provider = Build();
        var factory = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>()).Find(Diagram.Mindmap.Origin)!;

        // Act.
        var map = MindmapDocument.Parse(factory.CreateEmptyDocument("design"));

        // Assert.
        Assert.Equal("design", map.Root.Text);
        Assert.Empty(map.Root.Children);
    }
}
