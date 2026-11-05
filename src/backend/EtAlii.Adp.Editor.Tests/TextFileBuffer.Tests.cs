using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Tests;

/// <summary>
/// The buffer's contract (modular-text-editors Requirements 6.1, 6.3, 6.6, 7.1, 7.2): a file
/// saved unchanged comes back byte-identical - BOM, line endings, mixed endings and all -
/// and everything the family cannot honestly edit is refused with a reason, never guessed.
/// </summary>
public class TextFileBufferTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("text-file-buffer");
    private readonly string _root;

    public TextFileBufferTests()
    {
        _root = _testFolder.Path;
    }

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    private string Write(string name, byte[] bytes)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static TheoryData<string, byte[]> RoundTripFiles() => new()
    {
        { "utf8-bom.txt", [0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\n"u8] },
        { "utf8-no-bom.txt", "one\ntwo\n"u8.ToArray() },
        { "crlf.txt", "one\r\ntwo\r\nthree\r\n"u8.ToArray() },
        { "lf.txt", "one\ntwo\nthree\n"u8.ToArray() },
        { "mixed.txt", "one\r\ntwo\nthree\r\nfour"u8.ToArray() },
        { "no-trailing-newline.txt", "one\ntwo"u8.ToArray() },
        { "unicode.txt", [0xEF, 0xBB, 0xBF, .. "héllo → wörld\n"u8] },
    };

    /// <summary>The headline property: open, save the same content, and the bytes are identical.</summary>
    [Theory]
    [MemberData(nameof(RoundTripFiles))]
    public async Task SavingUnchangedContent_IsByteIdentical(string name, byte[] bytes)
    {
        // Arrange.
        var path = Write(name, bytes);
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);

        // Act.
        var error = await opened.Buffer.SaveAsync(opened.Buffer.Content, new FileWriter(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AChangedLine_KeepsEveryLinesOwnTerminator()
    {
        // Arrange: mixed endings, and only the middle line's text changes.
        var path = Write("mixed-edit.txt", "one\r\ntwo\nthree\r\n"u8.ToArray());
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);

        // Act.
        var error = await opened.Buffer.SaveAsync("one\r\nTWO\nthree\r\n", new FileWriter(), TestContext.Current.CancellationToken);

        // Assert: each line still carries the terminator it always had.
        Assert.Equal("", error);
        Assert.Equal("one\r\nTWO\nthree\r\n"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANewLine_GetsTheDominantTerminator()
    {
        // Arrange: CRLF-dominant file gains a line typed with a bare newline.
        var path = Write("grow.txt", "one\r\ntwo\r\n"u8.ToArray());
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);

        // Act.
        await opened.Buffer.SaveAsync("one\r\ntwo\r\nthree\n", new FileWriter(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("one\r\ntwo\r\nthree\r\n"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASave_PublishesThroughTheWriterItIsHanded()
    {
        // Arrange: a writer that records what it is handed rather than writing it (code-structure FR-068).
        byte[] before = [0xEF, 0xBB, 0xBF, .. "one\r\n"u8];
        var path = Write("handed.txt", before);
        var opened = TextFileBuffer.Open(path);
        Assert.NotNull(opened.Buffer);
        var writer = new RecordingWriter();

        // Act.
        var error = await opened.Buffer.SaveAsync("one\r\ntwo\r\n", writer, TestContext.Current.CancellationToken);

        // Assert: the writer was handed the bytes, byte-order mark and all, and nothing else wrote the file.
        Assert.Equal("", error);
        var saved = Assert.Single(writer.Saved);
        Assert.Equal(path, saved.Path);
        byte[] expected = [0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\n"u8];
        Assert.Equal(expected, saved.Content);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AFileOverTheLimit_IsRefusedBeforeBeingRead()
    {
        // Arrange: a sparse 6 MB file - written fast, and if the refusal happened after a
        // read, this test would be measurably slower than its peers.
        var path = IoPath.Combine(_root, "big.txt");
        using (var stream = File.Create(path))
        {
            stream.SetLength(TextFileBuffer.SizeLimitInBytes + 1);
        }

        // Act.
        var opened = TextFileBuffer.Open(path);

        // Assert: refused, with the file and the limit named (Requirement 6.6).
        Assert.Null(opened.Buffer);
        Assert.Contains("big.txt", opened.Refusal);
        Assert.Contains("5 MB", opened.Refusal);
    }

    [Fact]
    public void ABinaryFile_IsRefusedWithAClearMessage()
    {
        // Arrange.
        var path = Write("image.bin", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x1A]);

        // Act.
        var opened = TextFileBuffer.Open(path);

        // Assert (Requirements 7.1, 7.2).
        Assert.Null(opened.Buffer);
        Assert.Contains("binary", opened.Refusal);
    }

    [Fact]
    public void ANonUtf8File_IsRefusedRatherThanGuessed()
    {
        // Arrange: Latin-1 é (0xE9) is not valid UTF-8.
        var path = Write("latin1.txt", [(byte)'h', 0xE9, (byte)'!', (byte)'\n']);

        // Act.
        var opened = TextFileBuffer.Open(path);

        // Assert: refused with the way out named, never decoded into replacement characters.
        Assert.Null(opened.Buffer);
        Assert.Contains("UTF-8", opened.Refusal);
    }

    [Fact]
    public void TheBuffersFacts_NameWhatWasDetected()
    {
        // Arrange: BOM, CRLF, three lines - the property grid's four facts come from here
        // (modular-text-editors Requirement 9.1).
        var path = Write("facts.txt", [0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\nthree"u8]);

        // Act.
        var buffer = TextFileBuffer.Open(path).Buffer!;

        // Assert.
        Assert.Equal("UTF-8 with BOM", buffer.EncodingName);
        Assert.Equal("CRLF", buffer.LineEndingStyle);
        Assert.Equal(3, buffer.LineCount);
    }

    [Fact]
    public void MixedLineEndings_AreNamedHonestly()
    {
        // Arrange: two CRLF, one LF - a fact worth stating as it is, since the save keeps
        // each line's own terminator rather than normalising.
        var path = Write("mixed.txt", "one\r\ntwo\ntree\r\n"u8.ToArray());

        // Act.
        var buffer = TextFileBuffer.Open(path).Buffer!;

        // Assert.
        Assert.Equal("Mixed (mostly CRLF)", buffer.LineEndingStyle);
        Assert.Equal("UTF-8", buffer.EncodingName);
    }

    /// <summary>Records each save it is handed and writes nothing.</summary>
    private sealed class RecordingWriter : IFileWriter
    {
        public List<(string Path, byte[] Content)> Saved { get; } = [];

        public Task SaveAsync(string path, string content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The buffer saves bytes, so it keeps the byte-order mark the file arrived with.");

        public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken)
        {
            Saved.Add((path, content));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The buffer deletes nothing.");
    }
}
