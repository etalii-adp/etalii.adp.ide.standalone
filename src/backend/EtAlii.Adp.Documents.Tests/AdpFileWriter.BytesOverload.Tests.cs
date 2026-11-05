using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The bytes overload publishes exactly the bytes it was handed, through the same temp-then-replace
/// discipline as the string one.
/// </summary>
/// <remarks>
/// It exists for files ADP EDITS rather than authors: the string overload writes UTF-8 without a
/// BOM, which would silently strip the byte-order mark off somebody else's file on its first save
/// through the text editor. Before this, <c>TextFileBuffer</c> reached for its own FileStream to
/// keep the BOM - truncating the destination in place, at <c>FileShare.Read</c>, which a reader
/// could see half-written and a concurrent save could not replace.
/// <para>
/// The turn and the replace are not re-pinned here: both overloads reach them through one
/// <c>SaveCore</c>, which <c>AdpFileWriter.ConcurrentSaves.Tests</c> and
/// <c>AdpFileWriter.DeleteTakesTheTurn.Tests</c> already guard. What is new, and therefore what is
/// guarded here, is that the bytes arrive unaltered.
/// </para>
/// </remarks>
public class AdpFileWriterBytesOverloadTests : IDisposable
{
    private readonly FileWriter _writer = new();

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly TestFolder _testFolder = TestFolder.Create("adp-bytes-overload");
    private readonly string _folder;

    public AdpFileWriterBytesOverloadTests() => _folder = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ItWritesExactlyTheBytesItWasGiven_ByteOrderMarkIncluded()
    {
        var path = IoPath.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        byte[] content = [.. Bom, .. "héllo → wörld\r\n"u8];

        await _writer.SaveAsync(path, content, TestContext.Current.CancellationToken);

        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ItLeavesNoScratchFileBehind()
    {
        // The publish is temp-then-replace, so the scratch file is an implementation detail that
        // must not survive the save - the same property the string overload is held to.
        var path = IoPath.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);

        await _writer.SaveAsync(path, [.. Bom, .. "after"u8], TestContext.Current.CancellationToken);

        Assert.Empty(Directory.GetFiles(_folder, $"{AdpFileWriter.TempPrefix}*"));
    }

    [Fact]
    public async Task AReaderSharingDelete_DoesNotStopIt()
    {
        // What the raw FileStream could not do: a reader in SharedDocumentReader's own sharing mode
        // is reading the file, and the save still publishes underneath it.
        var path = IoPath.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        byte[] content = [.. Bom, .. "after"u8];

        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await _writer.SaveAsync(path, content, TestContext.Current.CancellationToken);
        }

        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANewFile_IsCreatedRatherThanRefused()
    {
        // A first publish has no destination to replace, which is a move rather than a replace.
        var path = IoPath.Combine(_folder, "fresh.txt");
        byte[] content = [.. Bom, .. "new"u8];

        await _writer.SaveAsync(path, content, TestContext.Current.CancellationToken);

        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
}
