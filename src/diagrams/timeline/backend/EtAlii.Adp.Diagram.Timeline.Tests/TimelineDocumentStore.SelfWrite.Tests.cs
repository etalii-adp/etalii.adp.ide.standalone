using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The control for the causal-loop store's self-write probe: the same saves and the same
/// concurrent reloads, against a store that ignores a reload of a path it is saving.
/// </summary>
/// <remarks>
/// <b>What this cannot see, stated because it was believed to cover it.</b> Every save here writes
/// the SAME text, so losing the in-memory entry costs nothing: the re-read document and the
/// discarded one are byte-identical. A discarded EDIT is therefore invisible to this test by
/// construction - it ran 2000 iterations of exactly this race and stayed green while
/// <c>TimelineFlowTests</c> lost an undo's edit under gate contention. The precondition it never
/// establishes is an edit that exists in memory and nowhere else, and
/// <see cref="TimelineDocumentStoreLostEditTests"/> is the test that does establish it.
/// <para>
/// The fetch-and-save on one line below says the same thing in code: nothing is ever edited
/// between the two, which is why the race this drives is the harmless half of it.
/// </para>
/// </remarks>
public class TimelineDocumentStoreSelfWriteTests : IDisposable
{
    private const string Text = "timeline: 1\nelements:\n  - id: aaa\n    label: Period\n    begin: 2026-01-05\n    end: 2026-02-13\n    row: 0\n";

    private readonly TestFolder _testFolder = TestFolder.Create("adp-timeline-store");
    private readonly string _workspace;

    public TimelineDocumentStoreSelfWriteTests() => _workspace = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Reload_ForItsOwnSaveAfterTheSaveReturned_TellsNobodyASecondTime()
    {
        // The watcher reports a write after it lands, so the notification for this store's own save
        // arrives once Save has returned. The save already told every session; a reload here would
        // re-read the file it just wrote and tell them all again.
        var path = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new TimelineDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        var changes = 0;
        store.Changed += (_, _) => changes++;
        Assert.Equal("", (await store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken)).Error);
        Assert.Equal(1, changes);

        await store.ReloadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(1, changes);

        // And the control: an edit from outside after that save still reaches the sessions.
        await File.WriteAllTextAsync(path, Text.Replace("Period", "Phase", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await store.ReloadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(2, changes);
        Assert.Contains("label: Phase", (await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reload_DuringItsOwnSaves_NeverReplacesTheDocumentWithWhatAPublishLeftBehind()
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new TimelineDocumentStore(new FileWriter());
        _ = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        var saving = 1;
        var damaged = 0;
        var reloads = 0;
        var reloader = Task.Run(async () =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: reading the latest `saving` is the point: the loop runs until the test's Volatile.Write(ref saving, 0) after its 2000 saves, and the test then awaits the reloader.
            while (Volatile.Read(ref saving) == 1)
            {
                await store.ReloadAsync(path, TestContext.Current.CancellationToken);
                reloads++;
                var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
                if (!entry.IsUsable || entry.Document.Text != Text)
                {
                    damaged++;
                }
            }
        }, TestContext.Current.CancellationToken);

        // Act.
        var refused = 0;
        for (var i = 0; i < 2000; i++)
        {
            if ((await store.SaveAsync(path, (await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken)).Failed)
            {
                refused++;
            }
        }

        Volatile.Write(ref saving, 0);
        await reloader;

        // Assert.
        Assert.True(reloads > 0, "The reloader never ran, so this run proves nothing.");
        Assert.True(damaged == 0 && refused == 0, $"{damaged} of {reloads} reloads left a damaged document; {refused} of 2000 saves were refused.");
    }
}
