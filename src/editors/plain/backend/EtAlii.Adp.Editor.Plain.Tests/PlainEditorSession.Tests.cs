using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Plain.Tests;

/// <summary>The plain session over real temp files: open, save, refuse (Requirements 3.1, 6.x, 7.x).</summary>
public class PlainEditorSessionTests : IDisposable
{
    private readonly string _root;

    public PlainEditorSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task ASession_ReadsAndSavesAFile()
    {
        // Arrange.
        var path = Write("a.txt", "hello\nworld\n"u8.ToArray());
        await using var session = new PlainEditorSession(path);

        // Act.
        var error = await session.SaveAsync("hello\nthere\n", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal("hello\nthere\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASessionOverABinaryFile_RefusesWithAReason()
    {
        // Arrange (Requirement 7.1/7.2).
        var path = Write("a.bin", [0x00, 0x01, 0x02, 0xFF]);
        await using var session = new PlainEditorSession(path);

        // Act and assert.
        Assert.Equal("", session.Content);
        Assert.Contains("binary", session.Refusal);
        Assert.NotEqual("", await session.SaveAsync("x", TestContext.Current.CancellationToken));
    }
}
