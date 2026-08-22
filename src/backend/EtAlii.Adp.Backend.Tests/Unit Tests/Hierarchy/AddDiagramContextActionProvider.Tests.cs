using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class AddDiagramContextActionProviderTests : IDisposable
{
    private static readonly DiagramDefinition SystemContext = new(new DiagramOrigin("c4", "context"), "System Context");
    private static readonly DiagramDefinition ClassDiagram = new(new DiagramOrigin("uml", "class"), "Class diagram");

    private readonly string _root;
    private readonly AddDiagramContextActionProvider _provider = new([SystemContext, ClassDiagram]);

    public AddDiagramContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ContextTarget FolderTarget(string path) => new(ContextScope.Hierarchy, path, IsContainer: true, ShortGuid.NewShortGuid());

    private static ContextTarget FileTarget(string path) => new(ContextScope.Hierarchy, path, IsContainer: false, ShortGuid.NewShortGuid());

    private string CreateFile(string name)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private string[] Listing() => Directory.GetFileSystemEntries(_root).Order(StringComparer.Ordinal).ToArray();

    // ---- discover ---------------------------------------------------------------------

    [Fact]
    public void Scope_IsHierarchy()
    {
        Assert.Equal(ContextScope.Hierarchy, _provider.Scope);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFolder_OffersAddWithTheInsertShortcut()
    {
        var groups = await _provider.DiscoverAsync(FolderTarget(_root), CancellationToken.None);

        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.Equal(AddDiagramContextActionProvider.AddActionId, action.Id);
        Assert.Equal("Add…", action.Label);
        Assert.Equal("mdi-plus", action.Icon);
        Assert.Equal("Insert", action.Shortcut?.Key);
        Assert.True(action.Available);
        Assert.Empty(action.UnavailableReason);
    }

    [Fact]
    public async Task DiscoverAsync_OnTheProjectRoot_OffersAdd()
    {
        // The root is a folder like any other to this provider; what makes it the root is the
        // service resolving "nothing selected" to it.
        var groups = await _provider.DiscoverAsync(FolderTarget(_root), CancellationToken.None);

        Assert.NotEmpty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFile_OffersNothing()
    {
        var groups = await _provider.DiscoverAsync(FileTarget(CreateFile("a.txt")), CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAVanishedFolder_OffersNothing()
    {
        var groups = await _provider.DiscoverAsync(FolderTarget(IoPath.Combine(_root, "gone")), CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoDiagramTypes_OffersAddUnavailableWithAReason()
    {
        var provider = new AddDiagramContextActionProvider([]);

        var groups = await provider.DiscoverAsync(FolderTarget(_root), CancellationToken.None);

        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.False(action.Available);
        Assert.Equal("No diagram types are available.", action.UnavailableReason);
    }

    [Fact]
    public async Task Provider_ReadsTheDefinitionsLazily_NotAtConstruction()
    {
        // The DI container builds the provider before Program.cs fills DiagramDefinition.All.
        // A list captured at construction would stay empty forever; the default must read the
        // cache at call time. Checked through the public seam: a list that changes after
        // construction is reflected.
        var definitions = new List<DiagramDefinition>();
        var provider = new AddDiagramContextActionProvider(definitions);
        var before = await provider.DiscoverAsync(FolderTarget(_root), CancellationToken.None);

        definitions.Add(SystemContext);
        var after = await provider.DiscoverAsync(FolderTarget(_root), CancellationToken.None);

        Assert.False(Assert.Single(Assert.Single(before).Actions).Available);
        Assert.True(Assert.Single(Assert.Single(after).Actions).Available);
    }

    // ---- execute ----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AsksForAChoice_WithTheTypeTreeAndTheAddLabel()
    {
        var result = await _provider.ExecuteAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, CancellationToken.None);

        var choice = Assert.IsType<ContextExecutionResult.RequiresChoice>(result);
        Assert.Equal("Add diagram", choice.Request.Title);
        Assert.Equal("mdi-plus", choice.Request.Icon);
        Assert.Equal("Add", choice.Request.ConfirmLabel);
        Assert.Equal("No diagram types are available.", choice.Request.EmptyMessage);
        Assert.Equal(["c4", "uml"], choice.Request.Options.Select(vendor => vendor.Label));
        Assert.Equal("c4/context", Assert.Single(choice.Request.Options[0].Children!).Id);
    }

    [Fact]
    public async Task ExecuteAsync_OnAVanishedFolder_Fails()
    {
        var result = await _provider.ExecuteAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, CancellationToken.None);

        var failed = Assert.IsType<ContextExecutionResult.Failed>(result);
        Assert.Equal("The folder no longer exists.", failed.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnUnknownAction_Fails()
    {
        var result = await _provider.ExecuteAsync(FolderTarget(_root), "hierarchy.something-else", CancellationToken.None);

        Assert.IsType<ContextExecutionResult.Failed>(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDiagramTypes_StillAsksForAChoice_WithAnEmptyTree()
    {
        // The dialog's own empty state is what the user sees; the menu normally prevents this.
        var provider = new AddDiagramContextActionProvider([]);

        var result = await provider.ExecuteAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, CancellationToken.None);

        var choice = Assert.IsType<ContextExecutionResult.RequiresChoice>(result);
        Assert.Empty(choice.Request.Options);
    }

    // ---- validate ---------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_AcceptsAnything_TheCommitStepJudgesTheChoice()
    {
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "whatever", CancellationToken.None);

        Assert.True(result.Valid);
    }

    // ---- commit: the seam ---------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_WithAKnownType_AnswersNotSupportedYet_NamingTheTitle()
    {
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal("Creating a System Context diagram is not supported yet.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_WritesNothingToDisk()
    {
        // Requirement 6.1: this spec creates, modifies and deletes nothing.
        CreateFile("existing.txt");
        var before = Listing();

        await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", CancellationToken.None);

        Assert.Equal(before, Listing());
    }

    [Fact]
    public async Task CommitAsync_WithAnUnknownOptionId_IsRejected()
    {
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "nope/nothing", CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal("That diagram type is not available.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_OnAVanishedFolder_ReportsTheFolder()
    {
        var result = await _provider.CommitAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, "c4/context", CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal("The folder no longer exists.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_ChecksTheFolderBeforeTheOptionId()
    {
        // Requirement 6.4 and create-diagram-file Requirement 1.3 depend on this order: a
        // vanished folder is reported as such even when the id is also bad.
        var result = await _provider.CommitAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, "nope/nothing", CancellationToken.None);

        Assert.Equal("The folder no longer exists.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_OnAFileTarget_ReportsTheFolder()
    {
        // A file is never a valid target; it reads as "no folder here" rather than leaking on.
        var result = await _provider.CommitAsync(FileTarget(CreateFile("a.txt")), AddDiagramContextActionProvider.AddActionId, "c4/context", CancellationToken.None);

        Assert.Equal("The folder no longer exists.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_WithAnUnknownAction_Fails()
    {
        var result = await _provider.CommitAsync(FolderTarget(_root), "hierarchy.something-else", "c4/context", CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("Unknown action", result.Error, StringComparison.Ordinal);
    }
}
