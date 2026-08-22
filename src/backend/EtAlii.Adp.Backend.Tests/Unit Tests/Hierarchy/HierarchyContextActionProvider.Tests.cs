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
    private readonly HierarchyContextActionProvider _provider = new();

    public HierarchyContextActionProviderTests()
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

    private static ContextTarget FileTarget(string path) =>
        new(ContextScope.Hierarchy, path, IsContainer: false, ShortGuid.NewShortGuid());

    private static ContextTarget FolderTarget(string path) =>
        new(ContextScope.Hierarchy, path, IsContainer: true, ShortGuid.NewShortGuid());

    private async Task<IReadOnlyList<ContextActionDefinition>> DiscoverAsync(ContextTarget target) =>
        (await _provider.DiscoverAsync(target, CancellationToken.None)).SelectMany(g => g.Actions).ToList();

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

        var groups = await _provider.DiscoverAsync(target, CancellationToken.None);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ExecuteAsync_ForRename_AsksForInputPrefilledWithTheCurrentName()
    {
        var target = FileTarget(CreateFile("current.txt"));

        var result = await _provider.ExecuteAsync(target, HierarchyContextActionProvider.RenameActionId, CancellationToken.None);

        var input = Assert.IsType<ContextExecutionResult.RequiresInput>(result);
        Assert.Equal("current.txt", input.Request.InitialValue);
    }

    [Fact]
    public async Task ExecuteAsync_ForDeletingAFolder_AsksToConfirmThatItsContentsGoToo()
    {
        var target = FolderTarget(CreateFolder("sub"));

        var result = await _provider.ExecuteAsync(target, HierarchyContextActionProvider.DeleteActionId, CancellationToken.None);

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

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, CancellationToken.None);

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

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, CancellationToken.None);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameIdenticalToTheCurrentOne()
    {
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "a.txt", CancellationToken.None);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsANameAlreadyTakenInTheSameFolder()
    {
        CreateFile("taken.txt");
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "taken.txt", CancellationToken.None);

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

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, value, CancellationToken.None);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsAnAbsolutePathPointingOutsideTheRoot()
    {
        var target = FileTarget(CreateFile("a.txt"));
        var outside = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", "outside.txt");

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, outside, CancellationToken.None);

        Assert.False(validation.Valid);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsANonEmptyValidAndActuallyDifferentName()
    {
        var target = FileTarget(CreateFile("a.txt"));

        var validation = await _provider.ValidateAsync(target, HierarchyContextActionProvider.RenameActionId, "b.txt", CancellationToken.None);

        Assert.True(validation.Valid);
    }

    [Fact]
    public async Task CommitAsync_RenamingAPopulatedFolder_PreservesItsEntireNestedContents()
    {
        CreateFolder("sub", "inner");
        File.WriteAllText(IoPath.Combine(_root, "sub", "inner", "leaf.txt"), "kept");
        File.WriteAllText(IoPath.Combine(_root, "sub", "top.txt"), "also kept");
        var target = FolderTarget(IoPath.Combine(_root, "sub"));

        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.RenameActionId, "renamed", CancellationToken.None);

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

        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.RenameActionId, "taken.txt", CancellationToken.None);

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

        var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.DeleteActionId, "", CancellationToken.None);

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
            var commit = await _provider.CommitAsync(target, HierarchyContextActionProvider.DeleteActionId, "", CancellationToken.None);

            Assert.False(commit.Completed);
            Assert.NotEqual("", commit.Error);
        }
    }

    // ---- the project root -------------------------------------------------------------
    //
    // The root is the folder the project *is*. It has a parent like any folder under a
    // temp path, so a parent-folder check does not protect it; it is recognised by carrying
    // no entry id, which no real entry ever lacks.

    private static ContextTarget RootTarget(string path) =>
        new(ContextScope.Hierarchy, path, IsContainer: true, SourceId: default);

    [Fact]
    public async Task DiscoverAsync_OnTheProjectRoot_ReportsRenameAndDeleteUnavailable_WithTheReason()
    {
        var groups = await _provider.DiscoverAsync(RootTarget(_root), CancellationToken.None);

        var actions = Assert.Single(groups).Actions;
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.False(action.Available));
        Assert.All(actions, action => Assert.Equal("The project folder itself cannot be renamed or deleted here.", action.UnavailableReason));
    }

    [Fact]
    public async Task ExecuteAsync_OnTheProjectRoot_Fails_ForBothActions()
    {
        var rename = await _provider.ExecuteAsync(RootTarget(_root), HierarchyContextActionProvider.RenameActionId, CancellationToken.None);
        var delete = await _provider.ExecuteAsync(RootTarget(_root), HierarchyContextActionProvider.DeleteActionId, CancellationToken.None);

        Assert.IsType<ContextExecutionResult.Failed>(rename);
        Assert.IsType<ContextExecutionResult.Failed>(delete);
    }

    [Fact]
    public async Task CommitAsync_OnTheProjectRoot_RefusesToDeleteIt_EvenWhenThePromptStepWasSkipped()
    {
        // The most important one: a deleted root is a destroyed project.
        File.WriteAllText(IoPath.Combine(_root, "keep.txt"), "x");

        var result = await _provider.CommitAsync(RootTarget(_root), HierarchyContextActionProvider.DeleteActionId, "", CancellationToken.None);

        Assert.False(result.Completed);
        Assert.True(Directory.Exists(_root));
        Assert.True(File.Exists(IoPath.Combine(_root, "keep.txt")));
    }

    [Fact]
    public async Task CommitAsync_OnTheProjectRoot_RefusesToRenameIt()
    {
        var result = await _provider.CommitAsync(RootTarget(_root), HierarchyContextActionProvider.RenameActionId, "renamed", CancellationToken.None);

        Assert.False(result.Completed);
        Assert.True(Directory.Exists(_root));
    }
}
