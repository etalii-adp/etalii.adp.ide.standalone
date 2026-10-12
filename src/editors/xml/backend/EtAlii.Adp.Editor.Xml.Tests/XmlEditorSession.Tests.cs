using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// The XML session over real temp files (xml-editor Requirement 8.1): everything plain does,
/// through the same shared buffer. What makes the module XML's is its analyser, not its session.
/// </summary>
public class XmlEditorSessionTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("xml-session");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ASession_ReadsAnXmlFile()
    {
        // Arrange.
        var path = _folder.Combine("notes.xml");
        await File.WriteAllBytesAsync(path, "<notes>\r\n  <note a = '1' />\r\n</notes>\r\n"u8.ToArray(), TestContext.Current.CancellationToken);

        // Act.
        await using var session = new XmlEditorSession(path);

        // Assert: read as written, CRLF endings included. Saving is the shared save command's.
        Assert.Equal("", session.Refusal);
        Assert.Equal("<notes>\r\n  <note a = '1' />\r\n</notes>\r\n", session.Content);
    }

    [Fact]
    public async Task AnUnchangedSave_IsByteIdentical()
    {
        // Arrange: the family's headline guarantee, held by this module too (Requirement 8.1: every space inside every tag included).
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "<?xml version=\"1.0\"?>\n<doc  x=\"1\" >line</doc>\n"u8];
        var path = _folder.Combine("doc.xml");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await using var session = new XmlEditorSession(path);

        // Act: what this module read, written back the way every editor's save writes it.
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);
        var error = await opened.Buffer.SaveAsync(session.Content, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
}
