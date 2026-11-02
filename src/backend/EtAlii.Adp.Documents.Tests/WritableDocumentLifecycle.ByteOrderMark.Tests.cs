using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A save keeps the byte-order mark the file had, and writes none where it had none: ADP edits a file somebody else wrote,
/// and the mark is part of its bytes (spec 071, T012's round trip of <c>tea.owm</c>).
/// </summary>
public sealed class WritableDocumentLifecycleByteOrderMarkTests : IDisposable
{
    private static readonly byte[] Mark = [0xEF, 0xBB, 0xBF];

    private readonly TestFolder _testFolder = TestFolder.Create("adp-lifecycle-bom");

    public void Dispose() => _testFolder.Dispose();

    [Fact]
    public async Task ASaveOfAFileWithAByteOrderMark_KeepsIt()
    {
        // Arrange.
        var path = IoPath.Combine(_testFolder.Path, "marked.txt");
        await File.WriteAllBytesAsync(path, [.. Mark, .. "before\n"u8.ToArray()], TestContext.Current.CancellationToken);
        var lifecycle = Lifecycle();
        await lifecycle.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var saved = await lifecycle.SaveAsync(path, "after\n", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(saved.Failed, saved.Error);
        byte[] expected = [.. Mark, .. "after\n"u8.ToArray()];
        Assert.Equal(expected, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASaveOfAFileWithoutOne_OrOfANewFile_WritesNone()
    {
        // Arrange.
        var plain = IoPath.Combine(_testFolder.Path, "plain.txt");
        var fresh = IoPath.Combine(_testFolder.Path, "fresh.txt");
        await File.WriteAllBytesAsync(plain, "before\n"u8.ToArray(), TestContext.Current.CancellationToken);
        var lifecycle = Lifecycle();
        await lifecycle.GetOrLoadAsync(plain, TestContext.Current.CancellationToken);

        // Act.
        await lifecycle.SaveAsync(plain, "after\n", TestContext.Current.CancellationToken);
        await lifecycle.SaveAsync(fresh, "new\n", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("after\n"u8.ToArray(), await File.ReadAllBytesAsync(plain, TestContext.Current.CancellationToken));
        Assert.Equal("new\n"u8.ToArray(), await File.ReadAllBytesAsync(fresh, TestContext.Current.CancellationToken));
    }

    private static WritableDocumentLifecycle<string> Lifecycle() => new((_, text) => text, text => text, new FileWriter());
}
