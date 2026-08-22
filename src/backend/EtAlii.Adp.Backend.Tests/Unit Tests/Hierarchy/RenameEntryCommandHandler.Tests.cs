using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class RenameEntryCommandHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly RenameEntryCommandHandler _handler = new();

    public RenameEntryCommandHandlerTests()
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
        => _handler.ExecuteAsync(new RenameEntryCommand(fullPath, newName));

    // ---- the happy paths ---------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RenamesAFile()
    {
        var path = CreateFile("old.txt");

        var result = await Rename(path, "new.txt");

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));
        Assert.Equal("content", File.ReadAllText(IoPath.Combine(_root, "new.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_RenamesAFolder()
    {
        var path = CreateFolder("old-folder");

        var result = await Rename(path, "new-folder");

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(path));
        Assert.True(Directory.Exists(IoPath.Combine(_root, "new-folder")));
    }

    [Fact]
    public async Task ExecuteAsync_RenamingAFolder_KeepsItsContents()
    {
        var folder = CreateFolder("old-folder");
        Directory.CreateDirectory(IoPath.Combine(folder, "nested"));
        File.WriteAllText(IoPath.Combine(folder, "nested", "child.txt"), "child");

        await Rename(folder, "new-folder");

        var moved = IoPath.Combine(_root, "new-folder", "nested", "child.txt");
        Assert.True(File.Exists(moved));
        Assert.Equal("child", File.ReadAllText(moved));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsAnInverseThatNamesTheNewPathAndTheOldName()
    {
        var path = CreateFile("old.txt");

        var result = await Rename(path, "new.txt");

        var inverse = Assert.IsType<RenameEntryCommand>(result.Inverse);
        Assert.Equal(IoPath.Combine(_root, "new.txt"), inverse.FullPath);
        Assert.Equal("old.txt", inverse.NewName);
    }

    [Fact]
    public async Task ExecuteAsync_TheReportedInverse_PutsTheFileBack()
    {
        var path = CreateFile("old.txt");
        var result = await Rename(path, "new.txt");

        var undone = await _handler.ExecuteAsync((RenameEntryCommand)result.Inverse!);

        Assert.True(undone.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(IoPath.Combine(_root, "new.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_ToleratesATrailingSeparatorOnAFolderPath()
    {
        var folder = CreateFolder("old-folder");

        var result = await Rename(folder + IoPath.DirectorySeparatorChar, "new-folder");

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(IoPath.Combine(_root, "new-folder")));

        // The inverse must still carry a usable name, which a trailing separator would have eaten.
        var inverse = Assert.IsType<RenameEntryCommand>(result.Inverse);
        Assert.Equal("old-folder", inverse.NewName);
    }

    [Fact]
    public async Task ExecuteAsync_ChangingOnlyCapitalisation_IsAllowed()
    {
        // On a case-insensitive filesystem the target "exists" because it is the source; that
        // must not be mistaken for a name collision.
        var path = CreateFile("readme.txt");

        var result = await Rename(path, "README.TXT");

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
        var path = CreateFile("old.txt");

        var result = await Rename(path, newName);

        Assert.False(result.IsSuccess);
        Assert.Equal("A name is required.", result.Error);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task ExecuteAsync_WithADotName_IsRejected(string newName)
    {
        var path = CreateFile("old.txt");

        var result = await Rename(path, newName);

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
        // Also the guard against a rename being used to move an entry out of its folder.
        var path = CreateFile("old.txt");

        var result = await Rename(path, newName);

        Assert.False(result.IsSuccess);
        Assert.Equal("A name cannot contain a path separator.", result.Error);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WithAnInvalidCharacterInTheName_IsRejected()
    {
        var path = CreateFile("old.txt");

        var result = await Rename(path, "bad\0name.txt");

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot contain", result.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WithTheSameNameItAlreadyHas_IsRejected()
    {
        var path = CreateFile("old.txt");

        var result = await Rename(path, "old.txt");

        Assert.False(result.IsSuccess);
        Assert.Equal("The new name is the same as the current name.", result.Error);
        Assert.True(File.Exists(path));
    }

    // ---- rejected targets --------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenAFileWithThatNameExists_IsRejectedWithoutOverwriting()
    {
        var path = CreateFile("old.txt", "original");
        CreateFile("taken.txt", "do not clobber");

        var result = await Rename(path, "taken.txt");

        Assert.False(result.IsSuccess);
        // Matched exactly, not by substring: the OS raises its own "...already exists" IOException
        // if the guard is removed, so a loose match would pass either way and prove nothing.
        Assert.Equal("'taken.txt' already exists in this folder.", result.Error);
        Assert.Equal("do not clobber", File.ReadAllText(IoPath.Combine(_root, "taken.txt")));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAFolderWithThatNameExists_IsRejected()
    {
        var path = CreateFile("old.txt");
        CreateFolder("taken");

        var result = await Rename(path, "taken");

        Assert.False(result.IsSuccess);
        Assert.Equal("'taken' already exists in this folder.", result.Error);
        Assert.True(File.Exists(path));
    }

    // ---- rejected sources --------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenTheEntryIsGone_IsRejected()
    {
        var missing = IoPath.Combine(_root, "never-existed.txt");

        var result = await Rename(missing, "new.txt");

        Assert.False(result.IsSuccess);
        Assert.Equal("The entry no longer exists.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithoutAPath_IsRejected(string fullPath)
    {
        var result = await Rename(fullPath, "new.txt");

        Assert.False(result.IsSuccess);
        Assert.Equal("No entry was given to rename.", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WithANullCommand_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.ExecuteAsync(null!));
    }

    [Fact]
    public async Task ExecuteAsync_WithAnAlreadyCancelledToken_Throws()
    {
        var path = CreateFile("old.txt");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.ExecuteAsync(new RenameEntryCommand(path, "new.txt"), cts.Token));

        Assert.True(File.Exists(path));
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
        var (stack, scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        var path = CreateFile("old.txt");

        var executed = await stack.ExecuteAsync(new RenameEntryCommand(path, "new.txt"));
        Assert.True(executed.IsSuccess);
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));

        var undone = await stack.UndoAsync();
        Assert.True(undone.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(IoPath.Combine(_root, "new.txt")));

        var redone = await stack.RedoAsync();
        Assert.True(redone.IsSuccess);
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ThroughTheHistoryStack_SeveralRenamesUnwindInReverseOrder()
    {
        var (stack, scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        CreateFile("first.txt");

        await stack.ExecuteAsync(new RenameEntryCommand(IoPath.Combine(_root, "first.txt"), "second.txt"));
        await stack.ExecuteAsync(new RenameEntryCommand(IoPath.Combine(_root, "second.txt"), "third.txt"));
        Assert.Equal(2, stack.UndoCount);

        await stack.UndoAsync();
        Assert.True(File.Exists(IoPath.Combine(_root, "second.txt")));

        await stack.UndoAsync();
        Assert.True(File.Exists(IoPath.Combine(_root, "first.txt")));
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ThroughTheHistoryStack_ARejectedRenameIsNotRecorded()
    {
        var (stack, scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        var path = CreateFile("old.txt");
        CreateFile("taken.txt");

        var result = await stack.ExecuteAsync(new RenameEntryCommand(path, "taken.txt"));

        Assert.False(result.IsSuccess);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ThroughTheHistoryStack_UndoFailsWhenTheEntryWasRenamedBehindOurBack()
    {
        // The undo has to re-validate: by the time it runs, the world may have moved on.
        var (stack, scope) = CreateHistory();
        using var scopeGuard = scope;
        using var stackGuard = stack;
        var path = CreateFile("old.txt");
        await stack.ExecuteAsync(new RenameEntryCommand(path, "new.txt"));

        File.Move(IoPath.Combine(_root, "new.txt"), IoPath.Combine(_root, "moved-by-someone-else.txt"));

        var undone = await stack.UndoAsync();

        Assert.False(undone.IsSuccess);
        Assert.Equal("The entry no longer exists.", undone.Error);
        Assert.True(stack.CanUndo);
    }
}
