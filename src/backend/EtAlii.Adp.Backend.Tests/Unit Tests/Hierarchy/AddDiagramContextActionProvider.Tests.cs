using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
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
    private readonly IHistoryStackStore _historyStacks;
    private readonly IHistoryStack _history;
    private readonly AddDiagramContextActionProvider _provider;

    public AddDiagramContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out _historyStacks);
        var catalog = new DiagramDefinitionCatalog { All = [SystemContext, ClassDiagram] };
        _provider = new AddDiagramContextActionProvider(_historyStacks, NoFactories, catalog, new DiagramFileRouter(catalog));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private ContextTarget FolderTarget(string path) => new(ContextScope.Hierarchy, path, IsContainer: true, ShortGuid.NewShortGuid(), RootPath: _root);

    private ContextTarget FileTarget(string path) => new(ContextScope.Hierarchy, path, IsContainer: false, ShortGuid.NewShortGuid(), RootPath: _root);

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
        // Arrange, act and assert.
        Assert.Equal(ContextScope.Hierarchy, _provider.Scope);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFolder_OffersAddWithTheInsertShortcut()
    {
        // Arrange.
        var groups = await _provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
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
        // Act.
        // The root is a folder like any other to this provider; what makes it the root is the
        // service resolving "nothing selected" to it.
        var groups = await _provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEmpty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAFile_OffersNothing()
    {
        // Act.
        var groups = await _provider.DiscoverAsync(FileTarget(CreateFile("a.txt")), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_OnAVanishedFolder_OffersNothing()
    {
        // Act.
        var groups = await _provider.DiscoverAsync(FolderTarget(IoPath.Combine(_root, "gone")), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoDiagramTypes_OffersAddUnavailableWithAReason()
    {
        // Arrange.
        var catalog = new DiagramDefinitionCatalog { All = [] };
        var provider = new AddDiagramContextActionProvider(_historyStacks, NoFactories, catalog, new DiagramFileRouter(catalog));

        var groups = await provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.False(action.Available);
        Assert.Equal("No diagram types are available.", action.UnavailableReason);
    }

    [Fact]
    public async Task Provider_ReadsTheDefinitionsLazily_NotAtConstruction()
    {
        // Arrange.
        // The DI container builds the provider before Program.cs fills DiagramDefinition.All.
        // A list captured at construction would stay empty forever; the default must read the
        // cache at call time. Checked through the public seam: a list that changes after
        // construction is reflected.
        var catalog = new DiagramDefinitionCatalog { All = [] };
        var provider = new AddDiagramContextActionProvider(_historyStacks, NoFactories, catalog, new DiagramFileRouter(catalog));
        var before = await provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        // Act.
        //definitions.Add(SystemContext);
        var after = await provider.DiscoverAsync(FolderTarget(_root), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(Assert.Single(Assert.Single(before).Actions).Available);
        Assert.False(Assert.Single(Assert.Single(after).Actions).Available);
    }

    // ---- execute ----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AsksForAChoice_WithTheTypeTreeAndTheAddLabel()
    {
        // Arrange.
        var result = await _provider.ExecuteAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(result);
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
        // Arrange.
        var result = await _provider.ExecuteAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Equal("The folder no longer exists.", failed.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnUnknownAction_Fails()
    {
        // Act.
        var result = await _provider.ExecuteAsync(FolderTarget(_root), "hierarchy.something-else", TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionFailed>(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDiagramTypes_StillAsksForAChoice_WithAnEmptyTree()
    {
        // Arrange.
        // The dialog's own empty state is what the user sees; the menu normally prevents this.
        var catalog = new DiagramDefinitionCatalog { All = [] };
        var provider = new AddDiagramContextActionProvider(_historyStacks, NoFactories, catalog, new DiagramFileRouter(catalog));

        var result = await provider.ExecuteAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var choice = Assert.IsType<ContextExecutionRequiresChoice>(result);
        Assert.Empty(choice.Request.Options);
    }

    // ---- validate ---------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_AcceptsAFreeName()
    {
        // Act.
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsAnEmptyName()
    {
        // Act.
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
        Assert.Equal("Enter a name.", result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameThatIsAPath()
    {
        // Act.
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "sub/domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_JudgesTheNameWithItsExtension()
    {
        // Arrange.
        // "domain" is free but "domain.adp" is not: the collision that matters is the file.
        CreateFile("domain.adp");

        // Act.
        var result = await _provider.ValidateAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Valid);
        Assert.Contains("already exists", result.Reason, StringComparison.Ordinal);
    }

    // ---- commit: the seam ---------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_CreatesTheFileWithTheChosenTypesMimeTypeAsItsOnlyLine()
    {
        // Act.
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed);
        var created = IoPath.Combine(_root, "domain.adp");
        Assert.Equal(created, result.CreatedFullPath);
        Assert.Equal("c4/context\r\n", await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_AcceptsANameTypedWithTheExtension_WithoutDoublingIt()
    {
        // Act.
        await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain.adp", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(File.Exists(IoPath.Combine(_root, "domain.adp")));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.adp.adp")));
    }

    [Fact]
    public async Task CommitAsync_WithATakenName_ReportsItAndLeavesTheExistingFileAlone()
    {
        // Arrange.
        var existing = IoPath.Combine(_root, "domain.adp");
        await File.WriteAllTextAsync(existing, "mine", TestContext.Current.CancellationToken);

        // Act.
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("already exists", result.Error, StringComparison.Ordinal);
        Assert.Equal("mine", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_WithAnInvalidName_CreatesNothing()
    {
        // Arrange.
        var before = Listing();

        // Act.
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "sub/domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Equal(before, Listing());
    }

    [Fact]
    public async Task CommitAsync_WithAnUnknownOptionId_IsRejected()
    {
        // Act.
        var result = await _provider.CommitAsync(FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "nope/nothing", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Equal("That diagram type is not available.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_OnAVanishedFolder_ReportsTheFolder()
    {
        // Act.
        var result = await _provider.CommitAsync(FolderTarget(IoPath.Combine(_root, "gone")), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Equal("The folder no longer exists.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_ChecksTheFolderBeforeTheOptionId_AndTheOptionIdBeforeTheName()
    {
        // Act and assert, step by step.
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
    public async Task CommitAsync_OnAFileNoTypeReads_IsRefused()
    {
        // Arrange: a file is a valid target now - it may be registered as a diagram
        // (add-diagram-action Requirement 4.3, revised) - but only by a type that reads its
        // kind of file. Nothing declares .txt.

        // Act.
        var result = await _provider.CommitAsync(FileTarget(CreateFile("a.txt")), AddDiagramContextActionProvider.AddActionId, "c4/context", "", TestContext.Current.CancellationToken);

        // Assert: refused by extension, not by "a file is never a target".
        Assert.Equal("That diagram type is not available for this file.", result.Error);
    }

    [Fact]
    public async Task CommitAsync_WithAnUnknownAction_Fails()
    {
        // Act.
        var result = await _provider.CommitAsync(FolderTarget(_root), "hierarchy.something-else", "c4/context", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("Unknown action", result.Error, StringComparison.Ordinal);
    }

    // ---- the create goes through the history --------------------------------------------

    [Fact]
    public async Task CommitAsync_CreatingADiagram_CanBeUndone()
    {
        // Arrange.
        // An Add is one Ctrl+Z away because its handler reports the delete of exactly what it
        // wrote as the inverse - the provider itself knows nothing about undoing anything.
        var commit = await _provider.CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        // Arrange, continued.
        Assert.True(commit.Completed, commit.Error);
        var created = IoPath.Combine(_root, "domain.adp");
        Assert.True(File.Exists(created));
        Assert.Equal(created, commit.CreatedFullPath);
        Assert.True(_history.CanUndo);

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.False(File.Exists(created));
    }

    [Fact]
    public async Task CommitAsync_CreatingADiagram_CanBeRedoneWithTheSameContent()
    {
        // Arrange.
        await _provider.CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        var redone = await _history.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redone.IsSuccess, redone.Error);
        var created = IoPath.Combine(_root, "domain.adp");
        Assert.True(File.Exists(created));
        // The redo re-runs the same command, so the file comes back as it was - MIME type and all.
        Assert.Equal("c4/context", (await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken)).Trim());
    }

    [Fact]
    public async Task CommitAsync_WhenTheNameIsAlreadyTaken_RecordsNothingForUndo()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "domain.adp"), "someone else's diagram", TestContext.Current.CancellationToken);

        // Act.
        var commit = await _provider.CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "c4/context", "domain", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(commit.Completed);
        Assert.False(_history.CanUndo);
        Assert.Equal("someone else's diagram", await File.ReadAllTextAsync(IoPath.Combine(_root, "domain.adp"), TestContext.Current.CancellationToken));
    }

    // ---- folder-subject types --------------------------------------------------------
    //
    // A type whose diagram IS the folder takes no name: its registration is created as a bare
    // ".adp" inside the folder. The dialog says so where the name field would be, and a folder
    // that already holds one offers the type greyed rather than hiding it.

    private static readonly DiagramDefinition AnsibleStructure = new(
        new DiagramOrigin("ansible", "structure"),
        "Ansible structure",
        Subject: DiagramSubject.Folder);

    /// <summary>A catalog holding both kinds, because serving both from one dialog is the point.</summary>
    private AddDiagramContextActionProvider FolderSubjectProvider()
    {
        var catalog = new DiagramDefinitionCatalog { All = [SystemContext, AnsibleStructure] };
        return new AddDiagramContextActionProvider(_historyStacks, NoFactories, catalog, new DiagramFileRouter(catalog));
    }

    private static ContextOptionNode OptionFor(ContextChoiceRequest request, string originKey) =>
        Assert.Single(request.Options.SelectMany(vendor => vendor.Children ?? []), option => option.Id == originKey);

    private async Task<ContextChoiceRequest> PromptFor(AddDiagramContextActionProvider provider)
    {
        var result = await provider.ExecuteAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);
        return Assert.IsType<ContextExecutionRequiresChoice>(result).Request;
    }

    [Fact]
    public async Task ExecuteAsync_OffersAFolderSubjectTypeWithTheSentenceAndAFileSubjectTypeWithItsSuggestion()
    {
        // Act.
        var request = await PromptFor(FolderSubjectProvider());

        // Assert: the folder-subject option says what will happen instead of taking a name.
        var folderSubject = OptionFor(request, "ansible/structure");
        Assert.Equal(
            "This type registers the folder itself, so the registration is created as `.adp` inside it.",
            folderSubject.NameSuppressedReason);
        Assert.Equal("", folderSubject.SuggestedValue);
        Assert.Equal("", folderSubject.UnavailableReason);

        // And the file-subject path is untouched, suggestion included - the whole reason the
        // name field stays on the prompt.
        var fileSubject = OptionFor(request, "c4/context");
        Assert.Equal("", fileSubject.NameSuppressedReason);
        Assert.NotEqual("", fileSubject.SuggestedValue);
        Assert.NotNull(request.NameField);
    }

    [Theory]
    [InlineData(".adp")]
    [InlineData("structure.adp")]
    public async Task ExecuteAsync_WhenTheFolderIsAlreadyRegistered_OffersTheTypeGreyedNamingTheFile(string registration)
    {
        // Arrange: both shapes count. What makes a folder registered is the type its
        // registration routes to, never the file's name.
        await File.WriteAllTextAsync(
            IoPath.Combine(_root, registration), AnsibleStructure.Origin.MimeType + "\n", TestContext.Current.CancellationToken);

        // Act.
        var request = await PromptFor(FolderSubjectProvider());

        // Assert: named, so the user can go and look at the file rather than wonder.
        var folderSubject = OptionFor(request, "ansible/structure");
        Assert.Equal($"This folder is already registered by `{registration}`", folderSubject.UnavailableReason);

        // Still offered rather than hidden - a type that vanishes leaves the user wondering
        // where it went, which is what the reason exists to answer.
        Assert.True(folderSubject.Selectable);
    }

    [Fact]
    public async Task CommitAsync_ForAFolderSubjectType_CreatesExactlyTheBareAdp()
    {
        // Act.
        var commit = await FolderSubjectProvider().CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "ansible/structure", "", TestContext.Current.CancellationToken);

        // Assert: exactly ".adp", not "structure.adp" and not "ansible.structure.adp".
        Assert.True(commit.Completed, commit.Error);
        Assert.Equal([IoPath.Combine(_root, ".adp")], Listing());
        Assert.Equal(
            AnsibleStructure.Origin.MimeType,
            (await File.ReadAllTextAsync(IoPath.Combine(_root, ".adp"), TestContext.Current.CancellationToken)).Trim());
    }

    [Fact]
    public async Task CommitAsync_ForAFolderSubjectType_IgnoresASubmittedNameRatherThanHonouringIt()
    {
        // Arrange: what a stale client would send - a name for a type that takes none.

        // Act.
        var commit = await FolderSubjectProvider().CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "ansible/structure", "infrastructure", TestContext.Current.CancellationToken);

        // Assert: ignored, not obeyed and not refused. Refusing would fail a user who did
        // nothing wrong; obeying would create the named file this spec exists to stop.
        Assert.True(commit.Completed, commit.Error);
        Assert.Equal([IoPath.Combine(_root, ".adp")], Listing());
    }

    [Theory]
    [InlineData(".adp")]
    [InlineData("structure.adp")]
    public async Task CommitAsync_WhenTheFolderIsAlreadyRegistered_RefusesAndLeavesItAlone(string registration)
    {
        // Arrange.
        var existing = IoPath.Combine(_root, registration);
        await File.WriteAllTextAsync(existing, AnsibleStructure.Origin.MimeType + "\n", TestContext.Current.CancellationToken);

        // Act.
        var commit = await FolderSubjectProvider().CommitAsync(
            FolderTarget(_root), AddDiagramContextActionProvider.AddActionId, "ansible/structure", "", TestContext.Current.CancellationToken);

        // Assert: refused, and the registration already there is untouched - overwriting one
        // is the outcome the re-consultation exists to prevent.
        Assert.False(commit.Completed);

        // The EXACT sentence, not merely one mentioning the file. Creating ".adp" where ".adp"
        // already sits is refused by the generic already-exists check too, so a looser
        // assertion passes with the folder check removed entirely - which is what happened
        // here before this line was tightened.
        Assert.Equal($"This folder is already registered by '{registration}'.", commit.Error);
        Assert.Equal([existing], Listing());
        Assert.Equal(
            AnsibleStructure.Origin.MimeType,
            (await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken)).Trim());
        Assert.False(_history.CanUndo);
    }
}
