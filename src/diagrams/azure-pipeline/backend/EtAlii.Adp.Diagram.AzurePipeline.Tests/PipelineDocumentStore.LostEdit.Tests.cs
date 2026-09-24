using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// A reload that arrives between a command's edit and its save must not discard the edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window is in <c>Save</c>'s shape.</b> A command does <c>GetOrLoad</c> - edit the
/// document in memory - <c>Save</c>, and <c>Save</c> calls <c>GetOrLoad</c> AGAIN rather than
/// writing the document the caller just edited. <c>Reload</c> drops the cached entry, so a reload
/// landing between those two steps means the object the command edited is no longer the object
/// that gets written: the save persists the re-read file AND REPORTS SUCCESS. The self-write guard
/// does not cover it, because it is cleared before the save's own reparse, so the notification for
/// the store's own write can arrive after the guard has gone and is then treated as external.
/// </para>
/// <para>
/// <b>What it costs, which is why this is not a flake.</b> The command reports success, the file
/// never receives the edit, and the command's INVERSE goes onto the undo stack for a change that
/// never happened - so the history is wrong rather than merely behind.
/// </para>
/// <para>
/// <b>Why the store tests beside this one cannot see it.</b> They save UNTOUCHED documents, or the
/// same text repeatedly. Losing the in-memory entry then costs nothing, because the re-read
/// document and the discarded one are byte-identical - so a discarded EDIT is invisible to them by
/// construction. Timeline's own self-write probe ran 2000 iterations of exactly this race and
/// stayed green while the integration flow failed. The precondition none of them establishes is an
/// edit that exists in memory and nowhere else, which is the first thing this test arranges.
/// </para>
/// <para>
/// <b>Deterministic on purpose.</b> The integration symptom needs gate contention to line the two
/// operations up. Calling <c>Reload</c> where the notification would land is the same ordering
/// without the wait, so this fails every time rather than one run in three.
/// </para>
/// </remarks>
public class PipelineDocumentStoreLostEditTests : IDisposable
{
    // The only two-argument store: its root travels with every call, so the guard has to
    // carry it too rather than borrow timeline's single-path shape.
    private readonly string _root;
    private readonly PipelineDocumentStore _store = new();

    public PipelineDocumentStoreLostEditTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "adp-lost-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASaveAfterAReload_WritesTheEditRatherThanTheFileItJustReread()
    {
        // Arrange: a real document of this notation, loaded and then edited in memory only.
        var path = IoPath.Combine(_root, "edge-anchors.yml");
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "edge-anchors.yml"), path);
        var document = _store.GetOrLoad(_root, path).Document;
        document.Insert(0, ["# lost-edit marker"]);

        // The edit is in memory and nowhere else yet, which is the precondition the store tests
        // beside this one never establish.
        Assert.Contains("# lost-edit marker", document.Text, StringComparison.Ordinal);

        // Act: the notification for an EARLIER write of this same file lands now. The store's own
        // guard is already cleared, so this is exactly what the reload bridge does with it.
        _store.Reload(_root, path);
        var error = _store.Save(_root, path);

        // Assert.
        Assert.Equal("", error);
        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // The save reported success, so the edit it reported success for has to be in the file.
        Assert.Contains("# lost-edit marker", written, StringComparison.Ordinal);
    }
}
