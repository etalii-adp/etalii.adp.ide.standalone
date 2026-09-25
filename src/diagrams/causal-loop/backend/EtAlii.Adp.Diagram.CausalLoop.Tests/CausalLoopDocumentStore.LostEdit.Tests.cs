using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// A reload that arrives between a command's edit and its save must not discard the edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the case the self-write probe beside it cannot see, and the reason is its own
/// arrangement rather than its coverage.</b>
/// <see cref="CausalLoopDocumentStoreSelfWriteTests"/> saves the SAME text repeatedly, so losing
/// the in-memory entry costs nothing: the re-read document and the one that was discarded are
/// byte-identical. A discarded EDIT is invisible to it by construction.
/// </para>
/// <para>
/// <b>The window is in <c>Save</c>'s shape.</b> <c>CausalLoopEdits.Run</c> does <c>GetOrLoad</c> -
/// mutate <c>entry.Document</c> in place through <c>CausalLoopWriter</c> - <c>Save(path)</c>, and
/// <c>Save(path)</c> looks the entry up in the cache AGAIN rather than writing the entry it was
/// handed. <c>Reload</c> replaces <c>_entries[path]</c>, so a reload landing between those two
/// steps means the document the command edited is no longer the document that gets written, and
/// the save persists the re-read file while returning <c>""</c> for success.
/// </para>
/// <para>
/// <b>Why that is worse than a refused save.</b> The command reports success, so its inverse goes
/// onto the undo stack for a change that never reached the disk.
/// </para>
/// </remarks>
public class CausalLoopDocumentStoreLostEditTests : IDisposable
{
    private const string Text =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable b \"Beta\"\r\n"
        + "link a -> b +\r\n";

    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-causal-loop-lost-edit-" + Guid.NewGuid().ToString("N"));

    public CausalLoopDocumentStoreLostEditTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASaveAfterAReload_WritesTheEditRatherThanTheFileItJustReread()
    {
        // Arrange: a document loaded and edited in memory, exactly as CausalLoopEdits.Run does it -
        // GetOrLoad, edit the entry's own document, then ask the store to save.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore();
        var entry = store.GetOrLoad(path);
        var refusal = CausalLoopWriter.AddVariable(entry.Document, entry.Model, "c", "Gamma");

        // A refusal here would make the rest of the test vacuous, so it is asserted rather than
        // assumed: without this line a writer that declined every edit would pass by doing nothing.
        Assert.Equal("", refusal);

        // The edit is in memory and nowhere else yet, which is this test's precondition.
        Assert.Contains("Gamma", entry.Document.Text, StringComparison.Ordinal);

        // Act: the notification for an EARLIER write of this same file lands now. The store's own
        // self-write guard is already cleared, so this is exactly what the reload bridge does.
        store.Reload(path);
        var error = store.Save(path, entry);

        // Assert.
        Assert.Equal("", error);
        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // The save reported success, so the edit it reported success for has to be in the file.
        Assert.Contains("Gamma", written, StringComparison.Ordinal);
    }
}
