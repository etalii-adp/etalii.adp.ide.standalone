using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

public class RenameEntryCommandHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly RenameEntryCommandHandler _handler = new(new EmptyCatalog());

    public RenameEntryCommandHandlerTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string CreateFile(string name, string content = "content")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateFolder(string name)
    {
        var path = IoPath.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private Task<CommandResult> Rename(string fullPath, string newName)
        => _handler.ExecuteAsync(new RenameEntryCommand(fullPath, newName), TestContext.Current.CancellationToken);

    // ---- the happy paths ---------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RenamesAFile()
    {
        // Arrange.
        var path = CreateFile("old.txt");

        // Act.
        var result = await Rename(path, "new.txt");

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));
        Assert.Equal("content", await File.ReadAllTextAsync(IoPath.Combine(_root, "new.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_RenamesAFolder()
    {
        // Arrange.
        var path = CreateFolder("old-folder");

        // Act.
        var result = await Rename(path, "new-folder");

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(path));
        Assert.True(Directory.Exists(IoPath.Combine(_root, "new-folder")));
    }

    [Fact]
    public async Task ExecuteAsync_RenamingAFolder_KeepsItsContents()
    {
        // Arrange.
        var folder = CreateFolder("old-folder");
        Directory.CreateDirectory(IoPath.Combine(folder, "nested"));
        await File.WriteAllTextAsync(IoPath.Combine(folder, "nested", "child.txt"), "child", TestContext.Current.CancellationToken);

        await Rename(folder, "new-folder");

        // Act and assert, step by step.
        var moved = IoPath.Combine(_root, "new-folder", "nested", "child.txt");
        Assert.True(File.Exists(moved));
        Assert.Equal("child", await File.ReadAllTextAsync(moved, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsAnInverseThatNamesTheNewPathAndTheOldName()
    {
        // Arrange.
        var path = CreateFile("old.txt");

        var result = await Rename(path, "new.txt");

        // Act and assert, step by step.
        var inverse = Assert.IsType<RenameEntryCommand>(result.Inverse);
        Assert.Equal(IoPath.Combine(_root, "new.txt"), inverse.FullPath);
        Assert.Equal("old.txt", inverse.NewName);
    }

    [Fact]
    public async Task ExecuteAsync_TheReportedInverse_PutsTheFileBack()
    {
        // Arrange.
        var path = CreateFile("old.txt");
        var result = await Rename(path, "new.txt");

        // Act.
        var undone = await _handler.ExecuteAsync((RenameEntryCommand)result.Inverse!, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(IoPath.Combine(_root, "new.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_ToleratesATrailingSeparatorOnAFolderPath()
    {
        // Arrange.
        var folder = CreateFolder("old-folder");

        var result = await Rename(folder + IoPath.DirectorySeparatorChar, "new-folder");

        // Act and assert, step by step.
        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(IoPath.Combine(_root, "new-folder")));

        // The inverse must still carry a usable name, which a trailing separator would have eaten.
        var inverse = Assert.IsType<RenameEntryCommand>(result.Inverse);
        Assert.Equal("old-folder", inverse.NewName);
    }

    [Fact]
    public async Task ExecuteAsync_ChangingOnlyCapitalisation_IsAllowed()
    {
        // Arrange.
        // On a case-insensitive filesystem the target "exists" because it is the source; that
        // must not be mistaken for a name collision.
        var path = CreateFile("readme.txt");

        // Act.
        var result = await Rename(path, "README.TXT");

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal(
            "README.TXT",
            IoPath.GetFileName(Directory.GetFiles(_root).Single()));
    }

    // ---- rejected names ----------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithoutAName_IsRejected(string newName)
    {
        // Arrange.
        var path = CreateFile("old.txt");

        // Act.
        var result = await Rename(path, newName);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("A name is required.", result.Error);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task ExecuteAsync_WithADotName_IsRejected(string newName)
    {
        // Arrange.
        var path = CreateFile("old.txt");

        // Act.
        var result = await Rename(path, newName);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData("sub/new.txt")]
    [InlineData("sub\\new.txt")]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    public async Task ExecuteAsync_WithASeparatorInTheName_IsRejected(string newName)
    {
        // Arrange.
        // Also the guard against a rename being used to move an entry out of its folder.
        var path = CreateFile("old.txt");

        // Act.
        var result = await Rename(path, newName);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("A name cannot contain a path separator.", result.Error);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WithAnInvalidCharacterInTheName_IsRejected()
    {
        // Arrange.
        var path = CreateFile("old.txt");

        // Act.
        var result = await Rename(path, "bad\0name.txt");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot contain", result.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WithTheSameNameItAlreadyHas_IsRejected()
    {
        // Arrange.
        var path = CreateFile("old.txt");

        // Act.
        var result = await Rename(path, "old.txt");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("The new name is the same as the current name.", result.Error);
        Assert.True(File.Exists(path));
    }

    // ---- rejected targets --------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenAFileWithThatNameExists_IsRejectedWithoutOverwriting()
    {
        // Arrange.
        var path = CreateFile("old.txt", "original");
        CreateFile("taken.txt", "do not clobber");

        // Act.
        var result = await Rename(path, "taken.txt");

        // Assert.
        Assert.False(result.IsSuccess);
        // Matched exactly, not by substring: the OS raises its own "...already exists" IOException
        // if the guard is removed, so a loose match would pass either way and prove nothing.
        Assert.Equal("'taken.txt' already exists in this folder.", result.Error);
        Assert.Equal("do not clobber", await File.ReadAllTextAsync(IoPath.Combine(_root, "taken.txt"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAFolderWithThatNameExists_IsRejected()
    {
        // Arrange.
        var path = CreateFile("old.txt");
        CreateFolder("taken");

        // Act.
        var result = await Rename(path, "taken");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("'taken' already exists in this folder.", result.Error);
        Assert.True(File.Exists(path));
    }

    // ---- rejected sources --------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenTheEntryIsGone_IsRejected()
    {
        // Arrange.
        var missing = IoPath.Combine(_root, "never-existed.txt");

        // Act.
        var result = await Rename(missing, "new.txt");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("The entry no longer exists.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithoutAPath_IsRejected(string fullPath)
    {
        // Act.
        var result = await Rename(fullPath, "new.txt");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("No entry was given to rename.", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WithANullCommand_Throws()
    {
        // Arrange, act and assert.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.ExecuteAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WithAnAlreadyCancelledToken_Throws()
    {
        // Arrange.
        var path = CreateFile("old.txt");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.ExecuteAsync(new RenameEntryCommand(path, "new.txt"), cts.Token));

        // Assert.
        Assert.True(File.Exists(path));
    }

    // ---- telling the hierarchy models ---------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenTheWatcherSeesTheMoveFirst_TheModelStillRaisesOneRenamedWithTheSameId()
    {
        // Arrange: a store whose watcher echo, Linux-shaped as Delete+Create, reaches the model
        // after the move but before the handler tells the store - the order the watcher thread
        // sometimes wins on a loaded CI runner.
        var path = CreateFile("original.txt");
        var store = new WatcherWinsTheRaceStore();
        var model = store.GetOrCreate(ShortGuid.NewShortGuid(), _root);
        var originalId = model.ListChildren(null).Single().Id;
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;
        var handler = new RenameEntryCommandHandler(new EmptyCatalog(), store);

        // Act.
        var result = await handler.ExecuteAsync(new RenameEntryCommand(path, "renamed.txt"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var renamed = Assert.IsType<HierarchyEntryRenamed>(Assert.Single(changes));
        Assert.Equal(originalId, renamed.EntryId);
        Assert.Equal("renamed.txt", renamed.NewName);
    }

    [Fact]
    public async Task ExecuteAsync_ARenameInAProject_IsFollowed_AndSoIsItsUndo()
    {
        // Arrange.
        var path = CreateFile("original.txt");
        var follower = new RecordingFollower();
        var handler = new RenameEntryCommandHandler(new EmptyCatalog(), followers: [follower]);

        // Act: renamed in a project, and undone by its inverse.
        var result = await handler.ExecuteAsync(new RenameEntryCommand(path, "renamed.txt", _root), TestContext.Current.CancellationToken);
        var inverse = Assert.IsType<RenameEntryCommand>(result.Inverse);
        var undone = await handler.ExecuteAsync(inverse, TestContext.Current.CancellationToken);

        // Assert: told of the move after it happened, and of the move back, each in the same project.
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(undone.IsSuccess, undone.Error);
        var renamed = IoPath.Combine(_root, "renamed.txt");
        Assert.Equal([(_root, path, renamed, true), (_root, renamed, path, true)], follower.Seen);
    }

    [Fact]
    public async Task ExecuteAsync_ARenameOutsideAProject_OrARefusedOne_IsNotFollowed()
    {
        // Arrange.
        var path = CreateFile("original.txt");
        CreateFile("taken.txt");
        var follower = new RecordingFollower();
        var handler = new RenameEntryCommandHandler(new EmptyCatalog(), followers: [follower]);

        // Act: one refused, and one made without a project.
        var refused = await handler.ExecuteAsync(new RenameEntryCommand(path, "taken.txt", _root), TestContext.Current.CancellationToken);
        var plain = await handler.ExecuteAsync(new RenameEntryCommand(path, "renamed.txt"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(refused.IsSuccess);
        Assert.True(plain.IsSuccess, plain.Error);
        Assert.Empty(follower.Seen);
    }

    /// <summary>Notes each move it is told of, and whether the entry was where it is said to be by then.</summary>
    private sealed class RecordingFollower : IEntryRenameFollower
    {
        public List<(string Root, string From, string To, bool Moved)> Seen { get; } = [];

        public void Renamed(string rootPath, string fromPath, string toPath) => Seen.Add((rootPath, fromPath, toPath, File.Exists(toPath) && !File.Exists(fromPath)));
    }

    [Fact]
    public async Task ExecuteAsync_ARefusedRename_LeavesNoExpectationBehind()
    {
        // Arrange: a rename refused after it was announced - a registration's subject portion
        // may not change - must not leave the old path's later, genuine delete looking like the
        // echo of a move.
        var path = CreateFile("test.first.adp");
        var store = new HierarchyModelStore();
        var model = store.GetOrCreate(ShortGuid.NewShortGuid(), _root);
        var originalId = model.ListChildren(null).Single().Id;
        var handler = new RenameEntryCommandHandler(new EmptyCatalog(), store);
        var refused = await handler.ExecuteAsync(new RenameEntryCommand(path, "prod.first.adp"), TestContext.Current.CancellationToken);
        Assert.False(refused.IsSuccess);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act.
        File.Delete(path);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, path, null);

        // Assert.
        var removed = Assert.IsType<HierarchyEntryRemoved>(Assert.Single(changes));
        Assert.Equal(originalId, removed.EntryId);
    }

    /// <summary>
    /// A real store whose models first receive the watcher's Linux-shaped echo of a move and only
    /// then the handler's notification - the watcher thread winning the race.
    /// </summary>
    private sealed class WatcherWinsTheRaceStore : IHierarchyModelStore
    {
        private readonly HierarchyModelStore _inner = new();
        private readonly List<HierarchyModel> _models = [];

        public HierarchyModel GetOrCreate(ShortGuid watchId, string rootPath)
        {
            var model = _inner.GetOrCreate(watchId, rootPath);
            _models.Add(model);
            return model;
        }

        public void AttachWatcher(ShortGuid watchId, RootFolderWatcher watcher) => _inner.AttachWatcher(watchId, watcher);

        public void Remove(ShortGuid watchId) => _inner.Remove(watchId);

        public void ExpectRename(string oldPath, string newPath) => _inner.ExpectRename(oldPath, newPath);

        public void WithdrawRename(string oldPath, string newPath) => _inner.WithdrawRename(oldPath, newPath);

        public void NotifyRenamed(string oldPath, string newPath)
        {
            foreach (var model in _models)
            {
                model.OnWatcherEvent(WatcherChangeTypes.Deleted, oldPath, null);
                model.OnWatcherEvent(WatcherChangeTypes.Created, null, newPath);
            }

            _inner.NotifyRenamed(oldPath, newPath);
        }
    }

    // ---- through the history stack ------------------------------------------------------

    private (HistoryStack Stack, IDisposable Scope) CreateHistory()
    {
        var services = new ServiceCollection()
            .AddSingleton<ICommandHandler<RenameEntryCommand>>(_handler)
            .BuildServiceProvider();

        return (new HistoryStack(new CommandDispatcher(services)), services);
    }

    [Fact]
    public async Task ThroughTheHistoryStack_ARenameCanBeUndoneAndRedone()
    {
        // Arrange.
        (HistoryStack stack, IDisposable scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        var path = CreateFile("old.txt");

        // Act and assert, step by step.
        var executed = await stack.ExecuteAsync(new RenameEntryCommand(path, "new.txt"), TestContext.Current.CancellationToken);
        Assert.True(executed.IsSuccess);
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));

        var undone = await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(undone.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(IoPath.Combine(_root, "new.txt")));

        var redone = await stack.RedoAsync(TestContext.Current.CancellationToken);
        Assert.True(redone.IsSuccess);
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ThroughTheHistoryStack_SeveralRenamesUnwindInReverseOrder()
    {
        // Arrange.
        (HistoryStack stack, IDisposable scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        CreateFile("first.txt");

        // Act and assert, step by step.
        await stack.ExecuteAsync(new RenameEntryCommand(IoPath.Combine(_root, "first.txt"), "second.txt"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new RenameEntryCommand(IoPath.Combine(_root, "second.txt"), "third.txt"), TestContext.Current.CancellationToken);
        Assert.Equal(2, stack.UndoCount);

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(IoPath.Combine(_root, "second.txt")));

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(IoPath.Combine(_root, "first.txt")));
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ThroughTheHistoryStack_ARejectedRenameIsNotRecorded()
    {
        // Arrange.
        (HistoryStack stack, IDisposable scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        var path = CreateFile("old.txt");
        CreateFile("taken.txt");

        // Act.
        var result = await stack.ExecuteAsync(new RenameEntryCommand(path, "taken.txt"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ThroughTheHistoryStack_UndoFailsWhenTheEntryWasRenamedBehindOurBack()
    {
        // Arrange.
        // The undo has to re-validate: by the time it runs, the world may have moved on.
        (HistoryStack stack, IDisposable scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        var path = CreateFile("old.txt");
        await stack.ExecuteAsync(new RenameEntryCommand(path, "new.txt"), TestContext.Current.CancellationToken);

        // Arrange, continued.
        File.Move(IoPath.Combine(_root, "new.txt"), IoPath.Combine(_root, "moved-by-someone-else.txt"));

        // Act.
        var undone = await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(undone.IsSuccess);
        Assert.Equal("The entry no longer exists.", undone.Error);
        Assert.True(stack.CanUndo);
    }
}

/// <summary>A catalog knowing no diagram types: every file is a plain file to the handlers.</summary>
internal sealed class EmptyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All { get; } = [];
}
