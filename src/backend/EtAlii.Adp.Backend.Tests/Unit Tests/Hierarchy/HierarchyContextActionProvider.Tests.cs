using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

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
        _provider = new HierarchyContextActionProvider(historyStacks);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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
        var target = FileTarget(CreateFile("a.txt"));

        var actions = await DiscoverAsync(target);

        var rename = actions.Single(a => a.Id == HierarchyContextActionProvider.RenameActionId);
        var delete = actions.Single(a => a.Id == HierarchyContextActionProvider.DeleteActionId);
        Assert.True(rename.Available);
        Assert.True(delete.Available);
        Assert.Equal("F2", rename.Shortcut?.Key);
        Assert.Equal("Delete", delete.Shortcut?.Key);
    }

    [Fact]
    public async Task DiscoverAsync_ForAFolder_ReportsTheSameTwoActions()
    {
        var target = FolderTarget(CreateFolder("sub"));

        var actions = await DiscoverAsync(target);

        Assert.Equal(
            new[] { HierarchyContextActionProvider.RenameActionId, HierarchyContextActionProvider.DeleteActionId },
            actions.Select(a => a.Id));
    }

    [Fact]
    public async Task DiscoverAsync_ForATargetThatNoLongerExists_ReportsNothingAtAll()
    {
        var target = FileTarget(IoPath.Combine(_root, "vanished.txt"));

        var groups = await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ExecuteAsync_ForRename_AsksForInputPrefilledWithTheCurrentName()
    {
        var target = FileTarget(CreateFile("current.txt"));

        var result = await _provider.ExecuteAsync(target, HierarchyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);

        var input = Assert.IsType<ContextExecutionResult.RequiresInput>(result);
        Assert.Equal("current.txt", input.Request.InitialValue);
    }

    [Fact]
    public async Task ExecuteAsync_ForDeletingAFolder_AsksToConfirmThatItsContentsGoToo()
    {
        var target = FolderTarget(CreateFolder("sub"));

        var result = await _provider.ExecuteAsync(target, HierarchyContextActionProvider.DeleteActionId, TestContext.Current.CancellationToken);

        var confirmation = Assert.IsType<ContextExecutionResult.RequiresConfirmation>(result);
        Assert.True(confirmation.Request.Danger);
        Assert.Contains("everything inside it", confirmation.Request.Message);
        Assert.Contains("cannot be undone", confirmation.Request.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_RejectsAnEmptyName(string value)
    {
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, TestContext.Current.CancellationToken);

        Assert.False(validation.Valid);
        Assert.NotEqual("", validation.Reason);
    }

    [Theory]
    [InlineData("bad:name.txt")]
    [InlineData("bad*name.txt")]
    [InlineData("bad?name.txt")]
    public async Task ValidateAsync_RejectsANameContainingCharactersTheFilesystemDisallows(string value)
    {
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, TestContext.Current.CancellationToken);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameIdenticalToTheCurrentOne()
    {
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "a.txt", TestContext.Current.CancellationToken);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameAlreadyTakenInTheSameFolder()
    {
        CreateFile("taken.txt");
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "taken.txt", TestContext.Current.CancellationToken);

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
        CreateFolder("sub");
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, TestContext.Current.CancellationToken);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsAnAbsolutePathPointingOutsideTheRoot()
    {
        var target = FileTarget(CreateFile("a.txt"));
        var outside = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", "outside.txt");

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, outside, TestContext.Current.CancellationToken);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsANonEmptyValidAndActuallyDifferentName()
    {
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "b.txt", TestContext.Current.CancellationToken);

        Assert.True(validation.Valid);
    }

    [Fact]
    public async Task CommitAsync_RenamingAPopulatedFolder_PreservesItsEntireNestedContents()
    {
        CreateFolder("sub", "inner");
        File.WriteAllText(IoPath.Combine(_root, "sub", "inner", "leaf.txt"), "kept");
        File.WriteAllText(IoPath.Combine(_root, "sub", "top.txt"), "also kept");
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.RenameActionId, "renamed", "", TestContext.Current.CancellationToken);

        Assert.True(commit.Completed);
        Assert.False(Directory.Exists(IoPath.Combine(_root, "sub")));
        Assert.Equal("kept", File.ReadAllText(IoPath.Combine(_root, "renamed", "inner", "leaf.txt")));
        Assert.Equal("also kept", File.ReadAllText(IoPath.Combine(_root, "renamed", "top.txt")));
    }

    [Fact]
    public async Task CommitAsync_WithAValueValidationRejects_DoesNotTouchTheFilesystem()
    {
        CreateFile("taken.txt", "original");
        var path = CreateFile("a.txt");
        var target = FileTarget(path);

        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.RenameActionId, "taken.txt", "", TestContext.Current.CancellationToken);

        Assert.False(commit.Completed);
        Assert.True(File.Exists(path));
        Assert.Equal("original", File.ReadAllText(IoPath.Combine(_root, "taken.txt")));
    }

    [Fact]
    public async Task CommitAsync_DeletingAPopulatedFolder_RemovesTheWholeSubtreeInOneGo()
    {
        CreateFolder("sub", "inner");
        File.WriteAllText(IoPath.Combine(_root, "sub", "inner", "leaf.txt"), "");
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        Assert.True(commit.Completed);
        Assert.False(Directory.Exists(IoPath.Combine(_root, "sub")));
    }

    [Fact]
    public async Task CommitAsync_DeletingAFolderHoldingALockedFile_ReportsThatTheDeleteDidNotComplete()
    {
        CreateFolder("sub");
        var lockedPath = IoPath.Combine(_root, "sub", "locked.txt");
        File.WriteAllText(lockedPath, "");
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        using (File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

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
        var groups = await _provider.DiscoverAsync(RootTarget(_root), TestContext.Current.CancellationToken);

        var actions = Assert.Single(groups).Actions;
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.False(action.Available));
        Assert.All(actions, action => Assert.Equal("The project folder itself cannot be renamed or deleted here.", action.UnavailableReason));
    }

    [Fact]
    public async Task ExecuteAsync_OnTheProjectRoot_Fails_ForBothActions()
    {
        var rename = await _provider.ExecuteAsync(RootTarget(_root), HierarchyContextActionProvider.RenameActionId, TestContext.Current.CancellationToken);
        var delete = await _provider.ExecuteAsync(RootTarget(_root), HierarchyContextActionProvider.DeleteActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionResult.Failed>(rename);
        Assert.IsType<ContextExecutionResult.Failed>(delete);
    }

    [Fact]
    public async Task CommitAsync_OnTheProjectRoot_RefusesToDeleteIt_EvenWhenThePromptStepWasSkipped()
    {
        // The most important one: a deleted root is a destroyed project.
        File.WriteAllText(IoPath.Combine(_root, "keep.txt"), "x");

        var result = await _provider.CommitAsync(RootTarget(_root), HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.True(Directory.Exists(_root));
        Assert.True(File.Exists(IoPath.Combine(_root, "keep.txt")));
    }

    [Fact]
    public async Task CommitAsync_OnTheProjectRoot_RefusesToRenameIt()
    {
        var result = await _provider.CommitAsync(RootTarget(_root), HierarchyContextActionProvider.RenameActionId, "renamed", "", TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.True(Directory.Exists(_root));
    }

    // ---- the actions go through the history --------------------------------------------

    [Fact]
    public async Task CommitAsync_Renaming_GoesThroughTheHistoryAndCanBeUndone()
    {
        // The whole point of routing the action through a command: the rename is reversible
        // without the provider knowing anything about how to reverse it.
        var path = CreateFile("before.txt", "content");

        var commit = await _provider.CommitAsync(
            FileTarget(path), HierarchyContextActionProvider.RenameActionId, "after.txt", "", TestContext.Current.CancellationToken);

        Assert.True(commit.Completed, commit.Error);
        Assert.True(_history.CanUndo);

        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(IoPath.Combine(_root, "after.txt")));
        Assert.Equal("content", File.ReadAllText(path));
    }

    [Fact]
    public async Task CommitAsync_Renaming_CanBeRedoneAfterAnUndo()
    {
        var path = CreateFile("before.txt");
        await _provider.CommitAsync(
            FileTarget(path), HierarchyContextActionProvider.RenameActionId, "after.txt", "", TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        var redone = await _history.RedoAsync(TestContext.Current.CancellationToken);

        Assert.True(redone.IsSuccess, redone.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "after.txt")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CommitAsync_Deleting_IsNotRecordedForUndo()
    {
        // A delete is deliberately one-way - the confirmation dialog says so - and what that
        // means concretely is that it leaves nothing on the undo stack to promise otherwise.
        var target = FileTarget(CreateFile("gone.txt"));

        var commit = await _provider.CommitAsync(
            target, HierarchyContextActionProvider.DeleteActionId, "", "", TestContext.Current.CancellationToken);

        Assert.True(commit.Completed, commit.Error);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public async Task CommitAsync_WhenTheCommandRejectsIt_ReportsTheHandlersOwnReason()
    {
        // The provider passes the handler's message straight through rather than inventing
        // one, so what the user reads is what actually stopped the change.
        var path = CreateFile("a.txt");
        File.Delete(path);

        var commit = await _provider.CommitAsync(
            FileTarget(path), HierarchyContextActionProvider.RenameActionId, "b.txt", "", TestContext.Current.CancellationToken);

        Assert.False(commit.Completed);
        Assert.Equal("The entry no longer exists.", commit.Error);
        Assert.False(_history.CanUndo);
    }
}
