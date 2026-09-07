using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Add on a file registers it: the file becomes a diagram of a type the user names, by writing
/// an <c>.adp</c> beside it (add-diagram-action Requirement 4.3 as revised, and
/// azure-pipeline-diagram Requirement 2.3). The file itself is never touched, which is the
/// property that matters when the file is executable configuration.
/// </summary>
public class AddDiagramContextActionProviderRegistrationTests : IDisposable
{
    private static readonly DiagramDefinition Pipeline =
        new(new DiagramOrigin("azure-devops", "pipeline"), "Azure DevOps pipeline", Extension: ".yml", SharedExtension: true);

    private static readonly DiagramDefinition Mindmap =
        new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private static readonly DiagramDefinition ClassDiagram =
        new(new DiagramOrigin("uml", "class"), "Class diagram");

    private static readonly DiagramDocumentFactories NoFactories = new([]);

    private readonly string _root;
    private readonly IHistoryStackStore _historyStacks;
    private readonly AddDiagramContextActionProvider _provider;

    public AddDiagramContextActionProviderRegistrationTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _ = TestHistory.Create(_root, out _historyStacks);
        var catalog = new DiagramDefinitionCatalog { All = [Pipeline, Mindmap, ClassDiagram] };
        _provider = new AddDiagramContextActionProvider(_historyStacks, NoFactories, catalog, new DiagramFileRouter(catalog));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string CreateFile(string name, string content = "stages:\n")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private ContextTarget FileTarget(string path) => new(ContextScope.Hierarchy, path, IsContainer: false, ShortGuid.NewShortGuid(), RootPath: _root);

    [Fact]
    public async Task DiscoverAsync_OnAFileATypeReads_OffersRegistration()
    {
        // Arrange.
        var target = FileTarget(CreateFile("azure-pipelines.yml"));

        // Act.
        var groups = await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken);

        // Assert: offered, and labelled as registering rather than adding.
        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.True(action.Available);
        Assert.Contains("as diagram", action.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFileNoTypeReads_OffersNothing()
    {
        // Arrange: offering a disabled action on every ordinary file would be noise.
        var target = FileTarget(CreateFile("notes.txt", "hello"));

        // Act.
        var groups = await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFileThatIsAlreadyRegistered_OffersNothing()
    {
        // Arrange: it is already a diagram, so it is opened rather than registered again.
        var path = CreateFile("azure-pipelines.yml");
        await File.WriteAllTextAsync(IoPath.ChangeExtension(path, ".adp"), "azure-devops/pipeline\n", TestContext.Current.CancellationToken);

        // Act.
        var groups = await _provider.DiscoverAsync(FileTarget(path), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task ExecuteAsync_OnAFile_OffersOnlyTheTypesThatReadIt_AndAsksNoName()
    {
        // Arrange.
        var target = FileTarget(CreateFile("azure-pipelines.yml"));

        // Act.
        var execution = await _provider.ExecuteAsync(target, AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        // Assert: one vendor, one type - the mindmap and the class diagram are not on offer,
        // and there is no name field because the file already has its name.
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(execution);
        Assert.Null(choice.Request.NameField);
        var vendor = Assert.Single(choice.Request.Options);
        var type = Assert.Single(vendor.Children!);
        Assert.Equal(Pipeline.Origin.Key, type.Id);
    }

    [Fact]
    public async Task CommitAsync_OnAFile_WritesTheAdpAndLeavesTheFileAlone()
    {
        // Arrange.
        const string body = "stages:\n  - stage: Build\n";
        var path = CreateFile("azure-pipelines.yml", body);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await _provider.CommitAsync(
            FileTarget(path), AddDiagramContextActionProvider.AddActionId, Pipeline.Origin.Key, "", TestContext.Current.CancellationToken);

        // Assert: the registration exists, names the type, and the pipeline is byte-identical.
        Assert.Equal("", result.Error);
        var adp = IoPath.ChangeExtension(path, ".adp");
        Assert.True(File.Exists(adp));
        Assert.Equal(Pipeline.Origin.MimeType, (await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken)).Trim());
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_OnAFile_WithATypeThatDoesNotReadIt_IsRefused()
    {
        // Arrange: a stale dialog, or one built against a different set of modules.
        var path = CreateFile("azure-pipelines.yml");

        // Act.
        var result = await _provider.CommitAsync(
            FileTarget(path), AddDiagramContextActionProvider.AddActionId, Mindmap.Origin.Key, "", TestContext.Current.CancellationToken);

        // Assert: refused, and nothing was written.
        Assert.Equal("That diagram type is not available for this file.", result.Error);
        Assert.False(File.Exists(IoPath.ChangeExtension(path, ".adp")));
    }

    [Fact]
    public async Task CommitAsync_OnAFile_IsOneUndoAway()
    {
        // Arrange.
        var path = CreateFile("azure-pipelines.yml");
        await _provider.CommitAsync(FileTarget(path), AddDiagramContextActionProvider.AddActionId, Pipeline.Origin.Key, "", TestContext.Current.CancellationToken);
        var adp = IoPath.ChangeExtension(path, ".adp");

        // Act.
        var undone = await _historyStacks.Get(_root).UndoAsync(TestContext.Current.CancellationToken);

        // Assert: the registration is gone and the pipeline is still there, untouched.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.False(File.Exists(adp));
        Assert.True(File.Exists(path));
    }

    // ------------------------------------------------------------------------------------------
    // The marker suggestion (owl-diagram Requirement 8.2): a definition may declare a body test,
    // and Add offers it exactly on the files whose text passes - a family's alternative reading
    // of a shared extension is suggested off its marker rather than on every family file.

    private static readonly DiagramDefinition MarkedReading = new(
        new DiagramOrigin("azure-devops", "release"),
        "Release pipeline",
        Extension: ".yml",
        SharedExtension: true,
        SuggestsBody: text => text.Contains("marker!", StringComparison.Ordinal));

    private AddDiagramContextActionProvider MarkerProvider()
    {
        var catalog = new DiagramDefinitionCatalog { All = [Pipeline, MarkedReading] };
        return new AddDiagramContextActionProvider(
            _historyStacks,
            NoFactories,
            catalog,
            new DiagramFileRouter(catalog));
    }

    [Fact]
    public async Task ExecuteAsync_OnAFileWithoutTheMarker_DoesNotOfferTheMarkedReading()
    {
        // Arrange.
        var target = FileTarget(CreateFile("plain.yml"));

        // Act.
        var execution = await MarkerProvider().ExecuteAsync(target, AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        // Assert: only the unconditional type is on offer.
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(execution);
        var vendor = Assert.Single(choice.Request.Options);
        var type = Assert.Single(vendor.Children!);
        Assert.Equal(Pipeline.Origin.Key, type.Id);
    }

    [Fact]
    public async Task ExecuteAsync_OnAFileCarryingTheMarker_OffersTheMarkedReadingToo()
    {
        // Arrange.
        var target = FileTarget(CreateFile("marked.yml", "stages: marker!\n"));

        // Act.
        var execution = await MarkerProvider().ExecuteAsync(target, AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        // Assert: both types are on offer under the shared vendor.
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(execution);
        var vendor = Assert.Single(choice.Request.Options);
        Assert.Equal(2, vendor.Children!.Count);
        Assert.Contains(vendor.Children!, node => node.Id == MarkedReading.Origin.Key);
    }
}
