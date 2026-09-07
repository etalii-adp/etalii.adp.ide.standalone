using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Drives the provider directly against a real temporary folder — no gRPC, no server —
/// since everything it decides is filesystem logic.
/// </summary>
public class HierarchyContextActionProviderTests : IDisposable
{
    private readonly string _root;
    private readonly IHistoryStack _history;
    private readonly HierarchyContextActionProvider _provider;

    public HierarchyContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out var historyStacks);
        _provider = new HierarchyContextActionProvider(historyStacks, new TestDiagramDefinitionCatalog());
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string CreateFile(string name, string content = "")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateFolder(params string[] segments)
    {
        var path = IoPath.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private ContextTarget FileTarget(string path) =>
        new(ContextScope.Hierarchy, path, IsContainer: false, ShortGuid.NewShortGuid(), RootPath: _root);

    private ContextTarget FolderTarget(string path) =>
        new(ContextScope.Hierarchy, path, IsContainer: true, ShortGuid.NewShortGuid(), RootPath: _root);

    private async Task<IReadOnlyList<ContextActionDefinition>> DiscoverAsync(ContextTarget target) =>
        (await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken)).SelectMany(g => g.Actions).ToList();

    [Fact]
    public async Task DiscoverAsync_ForAnExistingFile_ReportsRenameAndDeleteWithTheirShortcuts()
    {
        // Arrange.
        var target = FileTarget(CreateFile("a.txt"));

        var actions = await DiscoverAsync(target);

        // Act and assert, step by step.
        var rename = actions.Single(a => a.Id == HierarchyContextActionProvider.RenameActionId);
        var delete = actions.Single(a => a.Id == HierarchyContextActionProvider.DeleteActionId);
        Assert.True(rename.Available);
        Assert.True(delete.Available);
        Assert.Equal("F2", rename.Shortcut?.Key);
        Assert.Equal("Delete", delete.Shortcut?.Key);
    }

    [Fact]
    public async Task DiscoverAsync_ForAFolder_AlsoOffersToGrowASubfolder()
    {
        // Arrange.
        var target = FolderTarget(CreateFolder("sub"));

        // Act.
        var actions = await DiscoverAsync(target);

        // Assert.
        Assert.Equal(
            new[]
            {
                HierarchyContextActionProvider.RenameActionId,
                HierarchyContextActionProvider.DeleteActionId,
                HierarchyContextActionProvider.AddFolderActionId,
            },
            actions.Select(a => a.Id));
    }

    [Fact]
    public async Task DiscoverAsync_ForAFile_DoesNotOfferNewFolder()
    {
        // Arrange & act.
        var actions = await DiscoverAsync(FileTarget(CreateFile("a.txt")));

        // Assert.
        Assert.DoesNotContain(HierarchyContextActionProvider.AddFolderActionId, actions.Select(a => a.Id));
    }

    [Fact]
    public async Task CommitAsync_NewFolder_CreatesIt_AndUndoRemovesItAgain()
    {
        // Arrange.
        var target = FolderTarget(CreateFolder("sub"));

        // Act.
        var result = await _provider.CommitAsync(
            target, HierarchyContextActionProvider.AddFolderActionId, "child", "", TestContext.Current.CancellationToken);
        var created = IoPath.Combine(_root, "sub", "child");
        var existedAfterCommit = Directory.Exists(created);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed);
        Assert.True(existedAfterCommit);
        Assert.False(Directory.Exists(created));
    }

    [Fact]
    public async Task CommitAsync_NewFolder_OnTheProjectRootItself_IsAllowed()
    {
        // Arrange.
        // Rename and delete rightly refuse the root; an add acts WITHIN the folder, never on
        // it, and the root is where a project's first folder belongs.
        var root = new ContextTarget(ContextScope.Hierarchy, _root, IsContainer: true, SourceId: default, RootPath: _root);

        // Act.
        var result = await _provider.CommitAsync(
            root, HierarchyContextActionProvider.AddFolderActionId, "docs", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed);
        Assert.True(Directory.Exists(IoPath.Combine(_root, "docs")));
    }

    [Fact]
    public async Task UndoOfANewFolder_ThatGainedContentSince_IsRefused()
    {
        // Arrange.
        // An undo must never take work with it: once anything landed inside the new folder,
        // undoing its creation is refused rather than cascading.
        var target = FolderTarget(CreateFolder("sub"));
        await _provider.CommitAsync(
            target, HierarchyContextActionProvider.AddFolderActionId, "child", "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "sub", "child", "kept.txt"), "work", TestContext.Current.CancellationToken);

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(undone.IsSuccess);
        Assert.True(File.Exists(IoPath.Combine(_root, "sub", "child", "kept.txt")));
    }

    [Fact]
    public async Task ValidateAsync_NewFolder_RejectsANameAlreadyThere()
    {
        // Arrange.
        var target = FolderTarget(CreateFolder("sub"));
        CreateFolder("sub", "taken");

        // Act.
        var verdict = await _provider.ValidateAsync(
            target, HierarchyContextActionProvider.AddFolderActionId, "taken", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(verdict.Valid);
    }

    [Fact]
    public async Task DiscoverAsync_ForATargetThatNoLongerExists_ReportsNothingAtAll()
    {
        // Arrange.
        var target = FileTarget(IoPath.Combine(_root, "vanished.txt"));

        // Act.
        var groups = await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task ExecuteAsync_ForRename_AsksForInputPrefilledWithTheCurrentName()
    {
        // Arrange.
        var target = FileTarget(CreateFile("current.txt"));

        var result = await _provider.ExecuteAsync(target, HierarchyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var input = Assert.IsType<ContextExecutionRequiresInput>(result);
        Assert.Equal("current.txt", input.Request.InitialValue);
    }

    [Fact]
    public async Task ExecuteAsync_ForDeletingAFolder_AsksToConfirmThatItsContentsGoToo()
    {
        // Arrange.
        var target = FolderTarget(CreateFolder("sub"));

        var result = await _provider.ExecuteAsync(target, HierarchyContextActionProvider.DeleteActionId, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var confirmation = Assert.IsType<ContextExecutionRequiresConfirmation>(result);
        Assert.True(confirmation.Request.Danger);
        Assert.Contains("everything inside it", confirmation.Request.Message);
        Assert.Contains("cannot be undone", confirmation.Request.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_RejectsAnEmptyName(string value)
    {
        // Arrange.
        var target = FileTarget(CreateFile("a.txt"));

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(validation.Valid);
        Assert.NotEqual("", validation.Reason);
    }

    [Theory]
    [InlineData("bad:name.txt")]
    [InlineData("bad*name.txt")]
    [InlineData("bad?name.txt")]
    public async Task ValidateAsync_RejectsANameContainingCharactersTheFilesystemDisallows(string value)
    {
        // Arrange.
        var target = FileTarget(CreateFile("a.txt"));

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameIdenticalToTheCurrentOne()
    {
        // Arrange.
        var target = FileTarget(CreateFile("a.txt"));

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "a.txt", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameAlreadyTakenInTheSameFolder()
    {
        // Arrange.
        CreateFile("taken.txt");
        var target = FileTarget(CreateFile("a.txt"));

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "taken.txt", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(validation.Valid);
        Assert.Contains("already exists", validation.Reason);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    [InlineData("sub/nested.txt")]
    public async Task ValidateAsync_RejectsAValueThatWouldLeaveTheItemsOwnParentFolder(string value)
    {
        // Arrange.
        CreateFolder("sub");
        var target = FileTarget(CreateFile("a.txt"));

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsAnAbsolutePathPointingOutsideTheRoot()
    {
        // Arrange.
        var target = FileTarget(CreateFile("a.txt"));
        var outside = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", "outside.txt");

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, outside, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsANonEmptyValidAndActuallyDifferentName()
    {
        // Arrange.
        var target = FileTarget(CreateFile("a.txt"));

        // Act.
        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "b.txt", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(validation.Valid);
    }

    [Fact]
    public async Task CommitAsync_RenamingAPopulatedFolder_PreservesItsEntireNestedContents()
    {
        // Arrange.
        CreateFolder("sub", "inner");
        await File.WriteAllTextAsync(IoPath.Combine(_root, "sub", "inner", "leaf.txt"), "kept", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "sub", "top.txt"), "also kept", TestContext.Current.CancellationToken);
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        // Act.
        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.RenameActionId, "renamed", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        Assert.False(Directory.Exists(IoPath.Combine(_root, "sub")));
        Assert.Equal("kept", await File.ReadAllTextAsync(IoPath.Combine(_root, "renamed", "inner", "leaf.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("also kept", await File.ReadAllTextAsync(IoPath.Combine(_root, "renamed", "top.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_WithAValueValidationRejects_DoesNotTouchTheFilesystem()
    {
        // Arrange.
        CreateFile("taken.txt", "original");
        var path = CreateFile("a.txt");
        var target = FileTarget(path);

        // Act.
        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.RenameActionId, "taken.txt", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(commit.Completed);
        Assert.True(File.Exists(path));
        Assert.Equal("original", await File.ReadAllTextAsync(IoPath.Combine(_root, "taken.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_DeletingAPopulatedFolder_RemovesTheWholeSubtreeInOneGo()
    {
        // Arrange.
        CreateFolder("sub", "inner");
        await File.WriteAllTextAsync(IoPath.Combine(_root, "sub", "inner", "leaf.txt"), "", TestContext.Current.CancellationToken);
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        // Act.
        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        Assert.False(Directory.Exists(IoPath.Combine(_root, "sub")));
    }

    [Fact]
    public async Task CommitAsync_DeletingAFolderHoldingALockedFile_ReportsThatTheDeleteDidNotComplete()
    {
        // Windows-only by nature, not by neglect: FileShare.None is a mandatory lock only
        // there. Unix advisory semantics let the delete succeed, so the phenomenon this
        // pins - an honest report when the OS refuses - cannot occur on the Linux runner.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Mandatory file locks exist only on Windows.");

        // Arrange.
        CreateFolder("sub");
        var lockedPath = IoPath.Combine(_root, "sub", "locked.txt");
        await File.WriteAllTextAsync(lockedPath, "", TestContext.Current.CancellationToken);
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        // Act.
        using (File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
            Assert.False(commit.Completed);
            Assert.NotEqual("", commit.Error);
        }
    }

    // ---- the project root -------------------------------------------------------------
    //
    // The root is the folder the project *is*. It has a parent like any folder under a
    // temp path, so a parent-folder check does not protect it; it is recognised by carrying
    // no entry id, which no real entry ever lacks.

    private ContextTarget RootTarget(string path) =>
        new(ContextScope.Hierarchy, path, IsContainer: true, SourceId: default, RootPath: _root);

    [Fact]
    public async Task DiscoverAsync_OnTheProjectRoot_ReportsRenameAndDeleteUnavailable_WithTheReason()
    {
        // Arrange.
        var groups = await _provider.DiscoverAsync(RootTarget(_root), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var actions = groups.SelectMany(group => group.Actions).ToList();
        var untouchable = actions
            .Where(action => action.Id != HierarchyContextActionProvider.AddFolderActionId)
            .ToList();
        Assert.Equal(2, untouchable.Count);
        Assert.All(untouchable, action => Assert.False(action.Available));
        Assert.All(untouchable, action => Assert.Equal("The project folder itself cannot be renamed or deleted here.", action.UnavailableReason));

        // Adding INTO the root stays available: the add acts within the folder, never on it.
        var addFolder = Assert.Single(actions, action => action.Id == HierarchyContextActionProvider.AddFolderActionId);
        Assert.True(addFolder.Available);
    }

    [Fact]
    public async Task ExecuteAsync_OnTheProjectRoot_Fails_ForBothActions()
    {
        // Arrange and act.
        var rename = await _provider.ExecuteAsync(RootTarget(_root), HierarchyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var delete = await _provider.ExecuteAsync(RootTarget(_root), HierarchyContextActionProvider.DeleteActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionFailed>(rename);
        Assert.IsType<ContextExecutionFailed>(delete);
    }

    [Fact]
    public async Task CommitAsync_OnTheProjectRoot_RefusesToDeleteIt_EvenWhenThePromptStepWasSkipped()
    {
        // Arrange.
        // The most important one: a deleted root is a destroyed project.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "keep.txt"), "x", TestContext.Current.CancellationToken);

        // Act.
        var result = await _provider.CommitAsync(RootTarget(_root), HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.True(Directory.Exists(_root));
        Assert.True(File.Exists(IoPath.Combine(_root, "keep.txt")));
    }

    [Fact]
    public async Task CommitAsync_OnTheProjectRoot_RefusesToRenameIt()
    {
        // Act.
        var result = await _provider.CommitAsync(RootTarget(_root), HierarchyContextActionProvider.RenameActionId, "renamed", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.True(Directory.Exists(_root));
    }

    // ---- the actions go through the history --------------------------------------------

    [Fact]
    public async Task CommitAsync_Renaming_GoesThroughTheHistoryAndCanBeUndone()
    {
        // Arrange.
        // The whole point of routing the action through a command: the rename is reversible
        // without the provider knowing anything about how to reverse it.
        var path = CreateFile("before.txt", "content");

        // Arrange, continued.
        var commit = await _provider.CommitAsync(
            FileTarget(path), HierarchyContextActionProvider.RenameActionId, "after.txt", "", TestContext.Current.CancellationToken);

        // Arrange, continued.
        Assert.True(commit.Completed, commit.Error);
        Assert.True(_history.CanUndo);

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(IoPath.Combine(_root, "after.txt")));
        Assert.Equal("content", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_Renaming_CanBeRedoneAfterAnUndo()
    {
        // Arrange.
        var path = CreateFile("before.txt");
        await _provider.CommitAsync(
            FileTarget(path), HierarchyContextActionProvider.RenameActionId, "after.txt", "", TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        var redone = await _history.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redone.IsSuccess, redone.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "after.txt")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CommitAsync_Deleting_IsNotRecordedForUndo()
    {
        // Arrange.
        // A delete is deliberately one-way - the confirmation dialog says so - and what that
        // means concretely is that it leaves nothing on the undo stack to promise otherwise.
        var target = FileTarget(CreateFile("gone.txt"));

        // Act.
        var commit = await _provider.CommitAsync(
            target, HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed, commit.Error);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public async Task CommitAsync_WhenTheCommandRejectsIt_ReportsTheHandlersOwnReason()
    {
        // Arrange.
        // The provider passes the handler's message straight through rather than inventing
        // one, so what the user reads is what actually stopped the change.
        var path = CreateFile("a.txt");
        File.Delete(path);

        // Act.
        var commit = await _provider.CommitAsync(
            FileTarget(path), HierarchyContextActionProvider.RenameActionId, "b.txt", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(commit.Completed);
        Assert.Equal("The entry no longer exists.", commit.Error);
        Assert.False(_history.CanUndo);
    }
}
