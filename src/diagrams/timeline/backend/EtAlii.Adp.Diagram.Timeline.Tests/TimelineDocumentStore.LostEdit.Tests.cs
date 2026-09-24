using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// A reload that arrives between a command's splice and its save must not discard the splice.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the case the self-write probe beside it cannot see, and the reason is its own
/// arrangement rather than its coverage.</b>
/// <see cref="TimelineDocumentStoreSelfWriteTests.Reload_DuringItsOwnSaves_NeverReplacesTheDocumentWithWhatAPublishLeftBehind"/>
/// saves the SAME text two thousand times, so losing the in-memory entry costs nothing: the
/// re-read document and the one that was discarded are byte-identical. A discarded EDIT is
/// invisible to it by construction, which is why 2000 iterations of it stayed green while the
/// integration flow failed.
/// </para>
/// <para>
/// <b>The window is real and it is in <c>Save</c>'s shape.</b> A command does
/// <c>GetOrLoad</c> - splice through <c>TimelineWriter</c> - <c>Save</c>, and <c>Save(path)</c>
/// calls <c>GetOrLoad(path)</c> AGAIN rather than writing the entry it was handed. <c>Reload</c>
/// removes the cached entry, so a reload landing between those two steps means the object the
/// command spliced is no longer the object that gets written, and the save persists the re-read
/// file. The `_selfWrites` guard does not cover it: it is cleared in <c>Save</c>'s <c>finally</c>,
/// before the reparse, so the notification for the store's OWN write can arrive after the guard
/// has gone and is then treated as external.
/// </para>
/// <para>
/// <b>Deterministic on purpose.</b> The integration symptom - an undo that reports success and
/// leaves the moved dates in the file - needs gate contention to line the two up, and reproduced
/// once in about one run in three. Calling <c>Reload</c> where the notification would land is the
/// same ordering without the wait, so this fails every time rather than one time in three.
/// </para>
/// </remarks>
public class TimelineDocumentStoreLostEditTests : IDisposable
{
    private const string Text = "timeline: 1\nelements:\n  - id: aaa\n    label: Period\n    begin: 2026-01-05\n    end: 2026-02-13\n    row: 0\n";

    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-lost-edit-" + Guid.NewGuid().ToString("N"));

    public TimelineDocumentStoreLostEditTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASaveAfterAReload_WritesTheSpliceRatherThanTheFileItJustReread()
    {
        // Arrange: a document loaded and spliced in memory, exactly as SetTimelinePlacementCommandHandler
        // does it - GetOrLoad, splice through TimelineWriter, then ask the store to save.
        var path = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new TimelineDocumentStore();
        var entry = store.GetOrLoad(path);
        var element = entry.Model.Elements.Single(candidate => candidate.Id == "aaa");
        TimelineWriter.SetBegin(entry.Document, element, "2026-03-01");

        // The splice is in memory and nowhere else yet, which is the precondition this test needs
        // and the previous one never establishes.
        Assert.Contains("begin: 2026-03-01", entry.Document.Text, StringComparison.Ordinal);

        // Act: the notification for an EARLIER write of this same file lands now. The store's own
        // guard is already cleared, so this is exactly what the bridge does with it.
        store.Reload(path);
        var error = store.Save(path, entry);

        // Assert.
        Assert.Equal("", error);
        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // The save reported success, so the edit it reported success for has to be in the file.
        // A save that silently writes the old content is worse than a refusal: the command's
        // inverse goes on the undo stack for a change that never happened.
        Assert.Contains("begin: 2026-03-01", written, StringComparison.Ordinal);
    }
}
