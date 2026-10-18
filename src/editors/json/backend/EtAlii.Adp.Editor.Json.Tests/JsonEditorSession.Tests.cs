using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Editor.Json.Tests;

/// <summary>
/// The JSON session over real temp files (json-editor Requirement 8): everything plain does,
/// through the same shared buffer - which is the whole backend of this module.
/// </summary>
public class JsonEditorSessionTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("json-session");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ASession_ReadsAJsonFile()
    {
        // Arrange.
        var path = _folder.Combine("app.json");
        await File.WriteAllBytesAsync(path, "{\r\n  \"name\": \"app\"\r\n}\r\n"u8.ToArray(), TestContext.Current.CancellationToken);

        // Act.
        await using var session = new JsonEditorSession(path);

        // Assert: read as written, CRLF endings included. Saving is the shared save command's.
        Assert.Equal("", session.Refusal);
        Assert.Equal("{\r\n  \"name\": \"app\"\r\n}\r\n", session.Content);
    }

    [Fact]
    public async Task AnUnchangedSave_IsByteIdentical()
    {
        // Arrange: the family's headline guarantee (Requirement 8.1) - a byte-order mark, LF
        // endings and no final newline, none of which a save may add or take away.
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "{\n\t\"b\": 2,\n\t\"a\": 1\n}"u8];
        var path = _folder.Combine("doc.json");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await using var session = new JsonEditorSession(path);

        // Act: what this module read, written back the way every editor's save writes it.
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);
        var error = await opened.Buffer.SaveAsync(session.Content, new FileWriter(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
}
