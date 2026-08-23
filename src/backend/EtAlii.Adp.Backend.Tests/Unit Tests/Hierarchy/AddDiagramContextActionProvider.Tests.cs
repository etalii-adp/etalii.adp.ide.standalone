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
    private static readonly DiagramDocumentFactories NoFactories = new([]);
    private readonly IHistoryStack _history = TestHistory.Create();
    private readonly AddDiagramContextActionProvider _provider;

    public AddDiagramContextActionProviderTests()
    {
        _provider = new AddDiagramContextActionProvider(_history, NoFactories, [SystemContext, ClassDiagram]);
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
        var groups = await _provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

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
        var groups = await _provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        Assert.NotEmpty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFile_OffersNothing()
    {
        var groups = await _provider.DiscoverAsync(FileTarget(CreateFile("a.txt")), TestContext.Current.CancellationToken);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAVanishedFolder_OffersNothing()
    {
        var groups = await _provider.DiscoverAsync(FolderTarget(IoPath.Combine(_root, "gone")), TestContext.Current.CancellationToken);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoDiagramTypes_OffersAddUnavailableWithAReason()
    {
        var provider = new AddDiagramContextActionProvider(_history, NoFactories, []);

        var groups = await provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

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
        var provider = new AddDiagramContextActionProvider(_history, NoFactories, definitions);
        var before = await provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        definitions.Add(SystemContext);
        var after = await provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(Assert.Single(before).Actions).Available);
        Assert.True(Assert.Single(Assert.Single(after).Actions).Available);
    }

    // ---- execute ----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AsksForAChoice_WithTheTypeTreeAndTheAddLabel()
    {
        var result = await _provider.ExecuteAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

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
        var result = await _provider.ExecuteAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        var failed = Assert.IsType<ContextExecutionResult.Failed>(result);
        Assert.Equal("The folder no longer exists.", failed.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnUnknownAction_Fails()
    {
        var result = await _provider.ExecuteAsync(FolderTarget(_root), "hierarchy.something-else", TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionResult.Failed>(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDiagramTypes_StillAsksForAChoice_WithAnEmptyTree()
    {
        // The dialog's own empty state is what the user sees; the menu normally prevents this.
        var provider = new AddDiagramContextActionProvider(_history, NoFactories, []);

        var result = await provider.ExecuteAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        var choice = Assert.IsType<ContextExecutionResult.RequiresChoice>(result);
        Assert.Empty(choice.Request.Options);
    }

    // ---- validate ---------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_AcceptsAFreeName()
    {
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "domain", TestContext.Current.CancellationToken);

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsAnEmptyName()
    {
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "", TestContext.Current.CancellationToken);

        Assert.False(result.Valid);
        Assert.Equal("Enter a name.", result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameThatIsAPath()
    {
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "sub/domain", TestContext.Current.CancellationToken);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_JudgesTheNameWithItsExtension()
    {
        // "domain" is free but "domain.adp" is not: the collision that matters is the file.
        CreateFile("domain.adp");

        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "domain", TestContext.Current.CancellationToken);

        Assert.False(result.Valid);
        Assert.Contains("already exists", result.Reason, StringComparison.Ordinal);
    }

    // ---- commit: the seam ---------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_CreatesTheFileWithTheChosenTypesMimeTypeAsItsOnlyLine()
    {
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        var created = IoPath.Combine(_root, "domain.adp");
        Assert.Equal(created, result.CreatedFullPath);
        Assert.Equal("c4/context\n", File.ReadAllText(created));
    }

    [Fact]
    public async Task CommitAsync_AcceptsANameTypedWithTheExtension_WithoutDoublingIt()
    {
        await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain.adp", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(IoPath.Combine(_root, "domain.adp")));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.adp.adp")));
    }

    [Fact]
    public async Task CommitAsync_WithATakenName_ReportsItAndLeavesTheExistingFileAlone()
    {
        var existing = IoPath.Combine(_root, "domain.adp");
        File.WriteAllText(existing, "mine");

        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Contains("already exists", result.Error, StringComparison.Ordinal);
        Assert.Equal("mine", File.ReadAllText(existing));
    }

    [Fact]
    public async Task CommitAsync_WithAnInvalidName_CreatesNothing()
    {
        var before = Listing();

        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "sub/domain", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Equal(before, Listing());
    }

    [Fact]
    public async Task CommitAsync_WithAnUnknownOptionId_IsRejected()
    {
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "nope/nothing", "domain", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Equal("That diagram type is not available.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_OnAVanishedFolder_ReportsTheFolder()
    {
        var result = await _provider.CommitAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Equal("The folder no longer exists.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_ChecksTheFolderBeforeTheOptionId_AndTheOptionIdBeforeTheName()
    {
        // create-diagram-file Requirement 1.3 pins this order: the folder first, then the
        // type, then the name - each answered on its own terms rather than by whatever fails.
        var vanished = await _provider.CommitAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, "nope/nothing", "", TestContext.Current.CancellationToken);
        Assert.Equal("The folder no longer exists.", vanished.Error);

        var unknownType = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "nope/nothing", "", TestContext.Current.CancellationToken);
        Assert.Equal("That diagram type is not available.", unknownType.Error);

        var badName = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "", TestContext.Current.CancellationToken);
        Assert.Equal("Enter a name.", badName.Error);
    }

    [Fact]
    public async Task CommitAsync_OnAFileTarget_ReportsTheFolder()
    {
        // A file is never a valid target; it reads as "no folder here" rather than leaking on.
        var result = await _provider.CommitAsync(FileTarget(CreateFile("a.txt")), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.Equal("The folder no longer exists.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_WithAnUnknownAction_Fails()
    {
        var result = await _provider.CommitAsync(FolderTarget(_root), "hierarchy.something-else", "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Contains("Unknown action", result.Error, StringComparison.Ordinal);
    }

    // ---- the create goes through the history --------------------------------------------

    [Fact]
    public async Task CommitAsync_CreatingADiagram_CanBeUndone()
    {
        // An Add is one Ctrl+Z away because its handler reports the delete of exactly what it
        // wrote as the inverse - the provider itself knows nothing about undoing anything.
        var commit = await _provider.CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.True(commit.Completed, commit.Error);
        var created = IoPath.Combine(_root, "domain.adp");
        Assert.True(File.Exists(created));
        Assert.Equal(created, commit.CreatedFullPath);
        Assert.True(_history.CanUndo);

        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.False(File.Exists(created));
    }

    [Fact]
    public async Task CommitAsync_CreatingADiagram_CanBeRedoneWithTheSameContent()
    {
        await _provider.CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        var redone = await _history.RedoAsync(TestContext.Current.CancellationToken);

        Assert.True(redone.IsSuccess, redone.Error);
        var created = IoPath.Combine(_root, "domain.adp");
        Assert.True(File.Exists(created));
        // The redo re-runs the same command, so the file comes back as it was - MIME type and all.
        Assert.Equal("c4/context", File.ReadAllText(created).Trim());
    }

    [Fact]
    public async Task CommitAsync_WhenTheNameIsAlreadyTaken_RecordsNothingForUndo()
    {
        File.WriteAllText(IoPath.Combine(_root, "domain.adp"), "someone else's diagram");

        var commit = await _provider.CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        Assert.False(commit.Completed);
        Assert.False(_history.CanUndo);
        Assert.Equal("someone else's diagram", File.ReadAllText(IoPath.Combine(_root, "domain.adp")));
    }
}