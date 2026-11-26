using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// New… on the project: the ribbon's way to the Add dialog on the project root, for a new user who
/// pressed the ribbon's New and saw nothing happen (etalii.adp spec 092, host test T035).
/// </summary>
public class NewToolContextActionProviderTests : IDisposable
{
    private static readonly DiagramDefinition SystemContext = new(new ToolOrigin("c4", "context"), "System Context");

    private readonly TestFolder _testFolder = TestFolder.Create("new-tool");
    private readonly string _root;
    private readonly NewToolContextActionProvider _provider;

    public NewToolContextActionProviderTests()
    {
        _root = _testFolder.Path;
        TestHistory.Create(_root, out var historyStacks);
        var catalog = new DiagramDefinitionCatalog { All = [SystemContext] };
        _provider = new NewToolContextActionProvider(new AddDiagramContextActionProvider(historyStacks, new DiagramDocumentFactories([]), catalog, new DiagramFileRouter(catalog)));
    }

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    private ContextTarget ProjectTarget() => new(ContextScope.Project, _root, IsContainer: true, SourceId: default, Root: ProjectRoot.From(_root));

    [Fact]
    public async Task OnTheProject_OffersNew_AndAsksWhatAddAsksOnTheRoot()
    {
        // Act.
        var groups = await _provider.DiscoverAsync(ProjectTarget(), TestContext.Current.CancellationToken);
        var result = await _provider.ExecuteAsync(ProjectTarget(), NewToolContextActionProvider.NewActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(ContextScope.Project, _provider.Scope);
        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.Equal((NewToolContextActionProvider.NewActionId, "New…", true), (action.Id, action.Label, action.Available));
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(result);
        Assert.Equal("Add diagram", choice.Request.Title);
    }

    [Fact]
    public async Task Committed_CreatesTheChosenToolInTheProjectRoot()
    {
        // Act.
        var result = await _provider.CommitAsync(ProjectTarget(), NewToolContextActionProvider.NewActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(IoPath.Combine(_root, "domain.adp"), result.CreatedFullPath);
    }
}
