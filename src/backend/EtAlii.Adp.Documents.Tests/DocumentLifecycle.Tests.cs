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

    [Fact]
    public void AFirstOpenOfAMissingBody_OpensAsANewEmptyDocument()
    {
        var lifecycle = new DocumentLifecycle<Note>(Parse);

        var note = lifecycle.GetOrLoad(IoPath.Combine(_folder, "not-written-yet.note"));

        Assert.Equal("", note.Text);
    }

    [Fact]
    public void AFirstOpenOfAnUnreadableBody_OpensEmpty()
    {
        // R2.2's majority behaviour: open empty, with a warning naming the path.
        var path = Write("held.note", "content nobody can read right now");
        var lifecycle = new DocumentLifecycle<Note>(Parse);

        using var holder = Hold(path);
        var note = lifecycle.GetOrLoad(path);

        Assert.Equal("", note.Text);
    }

    [Fact]
    public void AFirstOpenThatCannotRead_IsTheModulesOwnState_WhenItDeclaresOne()
    {
        // R2.2's exception: a module that reports its own unavailability is told which case it is.
        var held = Write("held.note", "content");
        var missing = IoPath.Combine(_folder, "missing.note");
        var lifecycle = new DocumentLifecycle<Note>(Parse, Unavailable);

        using var holder = Hold(held);
        var unreadable = lifecycle.GetOrLoad(held);

        Assert.Equal("unavailable: Unreadable", unreadable.Text);
        Assert.Equal("unavailable: Missing", lifecycle.GetOrLoad(missing).Text);
    }

    [Fact]
    public void AReloadThatReads_InstallsTheNewDocument()
    {
        // The control for the keep-last-good tests below: without it, a Reload that never installed
        // anything would pass all of them.
        var path = Write("plan.note", "first");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var first = lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "second");

        var installed = lifecycle.Reload(path);

        Assert.True(installed);
        Assert.NotSame(first, lifecycle.GetOrLoad(path));
        Assert.Equal("second", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AReloadThatCannotRead_KeepsTheLastGoodDocument()
    {
        // R2.4, for every store. Before the shared lifecycle only c4 and causal-loop did this; six
        // stores installed an empty document, which put an empty canvas up whenever another program
        // was mid-save.
        var path = Write("plan.note", "good");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var good = lifecycle.GetOrLoad(path);

        using var holder = Hold(path);
        var installed = lifecycle.Reload(path);

        Assert.False(installed);
        Assert.Same(good, lifecycle.GetOrLoad(path));
    }

    [Fact]
    public void AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete()
    {
        // R2.5, and the pairing that makes keep-last-good safe. A reload that finds the body missing
        // cannot tell a publish in flight from a delete, so it keeps the last good document. Only the
        // watcher's BodyDeleted says the body is really gone - and then the document must NOT stay.
        // A store with keep-last-good and no BodyDeleted would keep a deleted diagram forever.
        var path = Write("plan.note", "good");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var good = lifecycle.GetOrLoad(path);
        File.Delete(path);

        var reloaded = lifecycle.Reload(path);

        Assert.False(reloaded);
        Assert.Same(good, lifecycle.GetOrLoad(path));

        var deleted = lifecycle.BodyDeleted(path);

        Assert.True(deleted);
        Assert.Equal("", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void ABodyDeleted_EndsAsTheModulesOwnMissingState_WhenItDeclaresOne()
    {
        var path = Write("query.note", "good");
        var lifecycle = new DocumentLifecycle<Note>(Parse, Unavailable);
        lifecycle.GetOrLoad(path);
        File.Delete(path);

        lifecycle.BodyDeleted(path);

        Assert.Equal("unavailable: Missing", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void ABodyDeletedThatIsAlreadyBack_IsReadAfresh()
    {
        // An editor that saves by deleting and re-creating: by the time the delete arrives, the file
        // on disk is the answer.
        var path = Write("plan.note", "before");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "after");

        lifecycle.BodyDeleted(path);

        Assert.Equal("after", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AParseThatThrowsOnReload_FailsThatReload_AndLeavesTheLastGoodDocument()
    {
        // Parsed before it is installed, so one document's broken text costs nothing that was cached.
        var path = Write("plan.note", "fine");
        var lifecycle = new DocumentLifecycle<Note>(ParseRefusingBoom);
        var good = lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "boom");

        Assert.Throws<FormatException>(() => lifecycle.Reload(path));

        Assert.Same(good, lifecycle.Get(path));
    }

    [Fact]
    public void Forget_MakesTheNextOpenReadTheFileAfresh()
    {
        var path = Write("plan.note", "first");
        var lifecycle = new DocumentLifecycle<Note>(Parse);
        var first = lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "second");

        lifecycle.Forget(path);

        Assert.Null(lifecycle.Get(path));
        Assert.NotSame(first, lifecycle.GetOrLoad(path));
        Assert.Equal("second", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AReloadRefusedOnce_StillDeliversTheChange()
    {
        // THE EDITORRESOLUTION FLAKE'S MECHANISM, pinned deterministically (traced by Developer 5,
        // 2026-09-25): a write's LAST event, a read refused once by another holder, and no later event
        // to re-read it. Without a retry the last good document is kept and the change is lost for good.
        var path = Write("plan.note", "first");
        var refusalsLeft = 0;
        var lifecycle = Scripted(read: file =>
        {
            if (refusalsLeft > 0)
            {
                refusalsLeft--;
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            return File.ReadAllText(file);
        });
        lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "second");
        refusalsLeft = 1;

        var installed = lifecycle.Reload(path);

        Assert.True(installed);
        Assert.Equal("second", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AReloadThatFindsTheBodyGoneForAnInstant_StillDeliversTheChange()
    {
        // A publish renames the body away and back; a read landing in that instant finds it missing.
        var path = Write("plan.note", "first");
        var vanishingsLeft = 0;
        var lifecycle = Scripted(read: file =>
        {
            if (vanishingsLeft > 0)
            {
                vanishingsLeft--;
                throw new FileNotFoundException("gone for an instant", file);
            }

            return File.ReadAllText(file);
        });
        lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "second");
        vanishingsLeft = 1;

        var installed = lifecycle.Reload(path);

        Assert.True(installed);
        Assert.Equal("second", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AReloadRefusedEveryTime_StopsAfterItsAttempts_AndKeepsTheLastGoodDocument()
    {
        // The retry is bounded, and keep-last-good is still what happens when it runs out. This is the
        // case a retry cannot rescue: it must neither spin forever nor install the failure.
        var path = Write("plan.note", "good");
        var reads = 0;
        var refusing = false;
        var lifecycle = Scripted(attempts: 3, read: file =>
        {
            reads++;
            return refusing ? throw new IOException("held") : File.ReadAllText(file);
        });
        var good = lifecycle.GetOrLoad(path);
        refusing = true;
        reads = 0;

        var installed = lifecycle.Reload(path);

        Assert.False(installed);
        Assert.Same(good, lifecycle.GetOrLoad(path));
        Assert.Equal(3, reads);
    }

    [Fact]
    public void AFirstOpenRefusedOnce_OpensTheRealDocument_NotAnEmptyOne()
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

        Assert.Equal("the real content", lifecycle.GetOrLoad(path).Text);
    }

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
        public string Text { get; set; } = text;
    }
}
