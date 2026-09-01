using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The save as a history command (modular-text-editors Requirement 6.2): shape-preserving on
/// the way down, and reversible - the inverse is the previous text through the very same
/// preserving path.
/// </summary>
public class SaveTextFileCommandHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly SaveTextFileCommandHandler _handler = new();

    public SaveTextFileCommandHandlerTests()
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

    [Fact]
    public async Task ASave_KeepsTheFilesOwnLineEndings()
    {
        // Arrange: a CRLF file - the write must not quietly restyle it (Requirement 6.1).
        var path = IoPath.Combine(_root, "notes.txt");
        await File.WriteAllBytesAsync(path, "one\r\ntwo\r\n"u8.ToArray(), TestContext.Current.CancellationToken);

        // Act.
        var result = await _handler.ExecuteAsync(new SaveTextFileCommand(path, "one\nchanged\n"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal("one\r\nchanged\r\n"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheInverse_PutsThePreviousTextBack()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "notes.txt");
        await File.WriteAllBytesAsync(path, "original\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        var saved = await _handler.ExecuteAsync(new SaveTextFileCommand(path, "changed\n"), TestContext.Current.CancellationToken);

        // Act: dispatch the reported inverse, the way undo would.
        var inverse = Assert.IsType<SaveTextFileCommand>(saved.Inverse);
        var undone = await _handler.ExecuteAsync(inverse, TestContext.Current.CancellationToken);

        // Assert: byte-identical to before the save, terminators included.
        Assert.True(undone.IsSuccess);
        Assert.Equal("original\r\n"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AVanishedFile_IsRefusedWithItsName()
    {
        // Arrange and act: redo may run long after the file went away.
        var result = await _handler.ExecuteAsync(
            new SaveTextFileCommand(IoPath.Combine(_root, "gone.txt"), "x"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("gone.txt", result.Error);
    }
}
