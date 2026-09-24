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
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task ASession_ReadsAMarkdownFile()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "readme.md");
        await File.WriteAllBytesAsync(path, "# Title\r\n\r\nBody\r\n"u8.ToArray(), TestContext.Current.CancellationToken);

        // Act.
        await using var session = new MarkdownEditorSession(path);

        // Assert: read as written, CRLF endings included. Saving is the shared save command's.
        Assert.Equal("", session.Refusal);
        Assert.Equal("# Title\r\n\r\nBody\r\n", session.Content);
    }

    [Fact]
    public async Task AnUnchangedSave_IsByteIdentical()
    {
        // Arrange: the family's headline guarantee, held by this module too (Requirement 10.2).
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "# Doc\nline\n"u8];
        var path = IoPath.Combine(_root, "doc.md");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await using var session = new MarkdownEditorSession(path);

        // Act: what this module read, written back the way every editor's save writes it.
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);
        var error = await opened.Buffer.SaveAsync(session.Content, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
}
