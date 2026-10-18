using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// Opening, keeping and re-reading a document, shared by every module store (backend-centralization R2).
/// </summary>
/// <remarks>
/// <b>These are the ONLY real witnesses to these behaviours once the stores are converted.</b> Before
/// the shared lifecycle, each store implemented them itself, so each store's own tests were an
/// independent witness. Afterwards every store runs this code, and their suites agree because they
/// cannot disagree. So these tests replace evidence the consolidation destroys; they don't merely add
/// to it.
/// </remarks>
public class DocumentLifecycleTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-lifecycle-" + Guid.NewGuid().ToString("N"));

    public DocumentLifecycleTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFirstOpenOfAMissingBody_OpensAsANewEmptyDocument(bool asynchronously)
    {
        var lifecycle = new DocumentLifecycle<Note>(Parse);

        var note = await GetOrLoad(lifecycle, IoPath.Combine(_folder, "not-written-yet.note"), asynchronously);

        Assert.Equal("", note.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFirstOpenOfAnUnreadableBody_OpensEmpty(bool asynchronously)
    {
        // R2.2's majority behaviour: open empty, with a warning naming the path.
        var path = Write("held.note", "content nobody can read right now");
        var lifecycle = new DocumentLifecycle<Note>(Parse);

        using var holder = Hold(path);
        var note = await GetOrLoad(lifecycle, path, asynchronously);

        Assert.Equal("", note.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFirstOpenThatCannotRead_IsTheModulesOwnState_WhenItDeclaresOne(bool asynchronously)
    {
        // R2.2's exception: a module that reports its own unavailability is told which case it is.
        var held = Write("held.note", "content");
        var missing = IoPath.Combine(_folder, "missing.note");
        var lifecycle = new DocumentLifecycle<Note>(Parse, Unavailable);

        using var holder = Hold(held);
        var unreadable = await GetOrLoad(lifecycle, held, asynchronously);

        Assert.Equal("unavailable: Unreadable", unreadable.Text);
        Assert.Equal("unavailable: Missing", (await GetOrLoad(lifecycle, missing, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatReads_InstallsTheNewDocument(bool asynchronously)
    {
        // The control for the keep-last-good tests below: without it, a Reload that never installed
        // anything would pass all of them.
        var path = Write("plan.note", "first");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var first = await GetOrLoad(lifecycle, path, asynchronously);
        await File.WriteAllTextAsync(path, "second", TestContext.Current.CancellationToken);

        var installed = await Reload(lifecycle, path, asynchronously);

        Assert.True(installed);
        Assert.NotSame(first, await GetOrLoad(lifecycle, path, asynchronously));
        Assert.Equal("second", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatCannotRead_KeepsTheLastGoodDocument(bool asynchronously)
    {
        // R2.4, for every store. Before the shared lifecycle only c4 and causal-loop did this; six
        // stores installed an empty document, which put an empty canvas up whenever another program
        // was mid-save.
        var path = Write("plan.note", "good");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var good = await GetOrLoad(lifecycle, path, asynchronously);

        using var holder = Hold(path);
        var installed = await Reload(lifecycle, path, asynchronously);

        Assert.False(installed);
        Assert.Same(good, await GetOrLoad(lifecycle, path, asynchronously));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete(bool asynchronously)
    {
        // R2.5, and the pairing that makes keep-last-good safe. A reload that finds the body missing
        // cannot tell a publish in flight from a delete, so it keeps the last good document. Only the
        // watcher's BodyDeleted says the body is really gone - and then the document must NOT stay.
        // A store with keep-last-good and no BodyDeleted would keep a deleted diagram forever.
        var path = Write("plan.note", "good");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var good = await GetOrLoad(lifecycle, path, asynchronously);
        File.Delete(path);

        var reloaded = await Reload(lifecycle, path, asynchronously);

        Assert.False(reloaded);
        Assert.Same(good, await GetOrLoad(lifecycle, path, asynchronously));

        var deleted = await BodyDeleted(lifecycle, path, asynchronously);

        Assert.True(deleted);
        Assert.Equal("", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABodyDeleted_EndsAsTheModulesOwnMissingState_WhenItDeclaresOne(bool asynchronously)
    {
        var path = Write("query.note", "good");
        var lifecycle = new DocumentLifecycle<Note>(Parse, Unavailable);
        await GetOrLoad(lifecycle, path, asynchronously);
        File.Delete(path);

        await BodyDeleted(lifecycle, path, asynchronously);

        Assert.Equal("unavailable: Missing", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABodyDeletedThatIsAlreadyBack_IsReadAfresh(bool asynchronously)
    {
        // An editor that saves by deleting and re-creating: by the time the delete arrives, the file
        // on disk is the answer.
        var path = Write("plan.note", "before");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        await GetOrLoad(lifecycle, path, asynchronously);
        await File.WriteAllTextAsync(path, "after", TestContext.Current.CancellationToken);

        await BodyDeleted(lifecycle, path, asynchronously);

        Assert.Equal("after", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AParseThatThrowsOnReload_FailsThatReload_AndLeavesTheLastGoodDocument(bool asynchronously)
    {
        // Parsed before it is installed, so one document's broken text costs nothing that was cached.
        var path = Write("plan.note", "fine");
        var lifecycle = new DocumentLifecycle<Note>(ParseRefusingBoom);
        var good = await GetOrLoad(lifecycle, path, asynchronously);
        await File.WriteAllTextAsync(path, "boom", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<FormatException>(async () => await Reload(lifecycle, path, asynchronously));

        Assert.Same(good, lifecycle.Get(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forget_MakesTheNextOpenReadTheFileAfresh(bool asynchronously)
    {
        var path = Write("plan.note", "first");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var first = await GetOrLoad(lifecycle, path, asynchronously);
        await File.WriteAllTextAsync(path, "second", TestContext.Current.CancellationToken);

        lifecycle.Forget(path);

        Assert.Null(lifecycle.Get(path));
        Assert.NotSame(first, await GetOrLoad(lifecycle, path, asynchronously));
        Assert.Equal("second", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadRefusedOnce_StillDeliversTheChange(bool asynchronously)
    {
        // THE EDITORRESOLUTION FLAKE'S MECHANISM, pinned deterministically (traced by Developer 5,
        // 2026-09-25): a write's LAST event, a read refused once by another holder, and no later event
        // to re-read it. Without a retry the last good document is kept and the change is lost for good.
        var path = Write("plan.note", "first");
        var refusalsLeft = 0;
        var lifecycle = Scripted(read: file =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            if (refusalsLeft > 0)
            {
                // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
                refusalsLeft--;
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            return File.ReadAllText(file);
        });
        await GetOrLoad(lifecycle, path, asynchronously);
        await File.WriteAllTextAsync(path, "second", TestContext.Current.CancellationToken);
        refusalsLeft = 1;

        var installed = await Reload(lifecycle, path, asynchronously);

        // The refusal was really met: without this, a lifecycle that never consulted the scripted
        // reader would pass by reading the file some other way.
        Assert.Equal(0, refusalsLeft);
        Assert.True(installed);
        Assert.Equal("second", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatFindsTheBodyGoneForAnInstant_StillDeliversTheChange(bool asynchronously)
    {
        // A publish renames the body away and back; a read landing in that instant finds it missing.
        var path = Write("plan.note", "first");
        var vanishingsLeft = 0;
        var lifecycle = Scripted(read: file =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            if (vanishingsLeft > 0)
            {
                // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
                vanishingsLeft--;
                throw new FileNotFoundException("gone for an instant", file);
            }

            return File.ReadAllText(file);
        });
        await GetOrLoad(lifecycle, path, asynchronously);
        await File.WriteAllTextAsync(path, "second", TestContext.Current.CancellationToken);
        vanishingsLeft = 1;

        var installed = await Reload(lifecycle, path, asynchronously);

        Assert.Equal(0, vanishingsLeft);
        Assert.True(installed);
        Assert.Equal("second", (await GetOrLoad(lifecycle, path, asynchronously)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadRefusedEveryTime_StopsAfterItsAttempts_AndKeepsTheLastGoodDocument(bool asynchronously)
    {
        // The retry is bounded, and keep-last-good is still what happens when it runs out. This is the
        // case a retry cannot rescue: it must neither spin forever nor install the failure.
        var path = Write("plan.note", "good");
        var reads = 0;
        var refusing = false;
        // THREE IS THE INJECTED BOUND, not production's five: this pins that the retry stops at whatever
        // count it is given. Production's own count is a choice recorded beside its constant.
        var lifecycle = Scripted(attempts: 3, read: file =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            reads++;
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            return refusing ? throw new IOException("held") : File.ReadAllText(file);
        });
        var good = await GetOrLoad(lifecycle, path, asynchronously);
        refusing = true;
        reads = 0;

        var installed = await Reload(lifecycle, path, asynchronously);

        Assert.False(installed);
        Assert.Same(good, await GetOrLoad(lifecycle, path, asynchronously));
        Assert.Equal(3, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFirstOpenRefusedOnce_OpensTheRealDocument_NotAnEmptyOne(bool asynchronously)
    {
        // Retried on a first open too: otherwise a holder keeping the file for a moment opens the diagram
        // EMPTY through R2.2's fallback, though the file on disk is fine.
        var path = Write("plan.note", "the real content");
        var refusalsLeft = 1;
        var lifecycle = Scripted(read: file =>
        {
            if (refusalsLeft > 0)
            {
                refusalsLeft--;
                throw new IOException("held");
            }

            return File.ReadAllText(file);
        });

        var opened = await GetOrLoad(lifecycle, path, asynchronously);

        // The first open really met the refusal, so this passes because of the retry rather than
        // because the open never reached the refusing reader.
        Assert.Equal(0, refusalsLeft);
        Assert.Equal("the real content", opened.Text);
    }

    [Fact]
    public async Task AnAsynchronousReadThatIsRefused_WaitsForItsNextAttemptOnTheLifecyclesClock()
    {
        // No thread sleeps between two attempts: the reload waits on the clock it was given, and
        // finishes only once that clock has moved (code-structure FR-050).
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Write("plan.note", "first");
        var clock = new FakeTimeProvider();
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refusalsLeft = 0;
        var lifecycle = new DocumentLifecycle<Note>(Parse, unavailable: null, file =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            if (refusalsLeft > 0)
            {
                // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
                refusalsLeft--;
                refused.TrySetResult();
                throw new IOException("held");
            }

            return File.ReadAllText(file);
        }, readAttempts: 3, TimeSpan.FromHours(1), clock);
        await lifecycle.GetOrLoadAsync(path, cancellationToken);
        await File.WriteAllTextAsync(path, "second", cancellationToken);
        refusalsLeft = 1;

        // Act: on another thread, so a reload that slept would hold that thread rather than this one.
        var reloading = Task.Run(() => lifecycle.ReloadAsync(path, cancellationToken).AsTask(), cancellationToken);
        await refused.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        // Assert: waiting for the next attempt until the clock moves, then delivering the change.
        Assert.False(reloading.IsCompleted, "The reload did not wait between its attempts.");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!reloading.IsCompleted && DateTime.UtcNow < deadline)
        {
            // Advanced until the wait is over: the reload may not have started its wait at the first advance.
            clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(10, cancellationToken);
        }

        Assert.True(reloading.IsCompleted, "The reload did not finish when its clock moved on.");
        Assert.True(await reloading);
        Assert.Equal("second", (await lifecycle.GetOrLoadAsync(path, cancellationToken)).Text);
    }

    // The synchronous reads and the asynchronous ones answer alike until the synchronous ones go
    // (code-structure T283), so every test runs through both.
    private static async Task<Note> GetOrLoad(DocumentLifecycle<Note> lifecycle, string path, bool asynchronously) =>
        asynchronously ? await lifecycle.GetOrLoadAsync(path, TestContext.Current.CancellationToken) : lifecycle.GetOrLoad(path);

    private static async Task<bool> Reload(DocumentLifecycle<Note> lifecycle, string path, bool asynchronously) =>
        asynchronously ? await lifecycle.ReloadAsync(path, TestContext.Current.CancellationToken) : lifecycle.Reload(path);

    private static async Task<bool> BodyDeleted(DocumentLifecycle<Note> lifecycle, string path, bool asynchronously) =>
        asynchronously ? await lifecycle.BodyDeletedAsync(path, TestContext.Current.CancellationToken) : lifecycle.BodyDeleted(path);

    // Lambdas with discards rather than methods, so no parameter is left unused.
    private static readonly Func<string, string, Note> Parse = (_, text) => new Note(text);

    private static readonly Func<string, string, Note> ParseRefusingBoom =
        (_, text) => text == "boom" ? throw new FormatException("boom") : new Note(text);

    private static readonly Func<string, DocumentUnavailability, string, Note> Unavailable =
        (_, unavailability, _) => new Note($"unavailable: {unavailability}");

    // A holder that shares nothing, so the lifecycle's shared read is refused while the file stays
    // present - the unreadable-but-present case. Windows-only by nature: FileShare is mandatory there.
    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.None);

    // A lifecycle whose read refuses exactly as the test scripts it, retried without waiting.
    private static DocumentLifecycle<Note> Scripted(Func<string, string> read, int attempts = 3) =>
        new(Parse, unavailable: null, read, attempts, TimeSpan.Zero);

    private string Write(string name, string text)
    {
        var path = IoPath.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private sealed class Note(string text)
    {
        public string Text { get; } = text;
    }
}
