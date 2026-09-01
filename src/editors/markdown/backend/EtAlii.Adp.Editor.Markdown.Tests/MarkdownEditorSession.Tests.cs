using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Markdown.Tests;

/// <summary>
/// The markdown session over real temp files (Requirements 10.1, 10.2): everything plain
/// does, through the same shared buffer - which is the whole backend of this module, per
/// Requirement 10.3's second-module-needs-nothing claim.
/// </summary>
public class MarkdownEditorSessionTests : IDisposable
{
    private readonly string _root;

    public MarkdownEditorSessionTests()
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
    public async Task ASession_ReadsAndSavesAMarkdownFile()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "readme.md");
        await File.WriteAllBytesAsync(path, "# Title\r\n\r\nBody\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await using var session = new MarkdownEditorSession(path);

        // Act.
        var error = await session.SaveAsync("# Title\r\n\r\nChanged\r\n", TestContext.Current.CancellationToken);

        // Assert: saved, with the CRLF endings the file arrived with.
        Assert.Equal("", error);
        Assert.Equal("# Title\r\n\r\nChanged\r\n"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnchangedSave_IsByteIdentical()
    {
        // Arrange: the family's headline guarantee, held by this module too (Requirement 10.2).
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "# Doc\nline\n"u8];
        var path = IoPath.Combine(_root, "doc.md");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await using var session = new MarkdownEditorSession(path);

        // Act.
        var error = await session.SaveAsync(session.Content, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
}
