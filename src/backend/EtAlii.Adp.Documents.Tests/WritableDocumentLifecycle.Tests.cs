using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// Saving through the shared lifecycle: writing what the caller edited, and not hearing its own
/// write as somebody else's (backend-centralization R2.3, R3.4, and task 5's save-path constraint).
/// </summary>
public class WritableDocumentLifecycleTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-writable-lifecycle-" + Guid.NewGuid().ToString("N"));

    public WritableDocumentLifecycleTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ASaveAfterAReload_WritesTheEditRatherThanTheFileItJustReread()
    {
        // THE BACKSTOP to the save's signature. Every one of the nine writable stores once lost an
        // edit exactly this way: a command edited the cached document, a reload landed before its save
        // and replaced the cache entry, and the save wrote whatever the cache held - the re-read file -
        // while reporting success. Taking the document as an argument makes that unwritable; this test
        // is what notices if someone writes it anyway, inside the save.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle();
        var note = lifecycle.GetOrLoad(path);
        note.Text = "edited";

        // The notification for an EARLIER write of this same file lands now. The reload reads
        // "before" and installs a new document, so the cache no longer holds the edited one.
        var reloaded = lifecycle.Reload(path);
        Assert.True(reloaded);
        Assert.NotSame(note, lifecycle.Get(path));

        var saved = lifecycle.Save(path, note);

        Assert.False(saved.Failed);
        Assert.Equal("edited", File.ReadAllText(path));
        Assert.Same(note, lifecycle.Get(path));
    }

    [Fact]
    public void AReloadDuringItsOwnSave_IsIgnored()
    {
        // R2.3. The write is stood inside, and the reload a watcher would raise for it arrives there.
        // The file still holds the OLD text at that moment, so an unguarded reload would read it and
        // install a new document - which is what makes this observable rather than vacuous.
        var path = Write("plan.note", "before");
        bool? reloadedDuringSave = null;
        WritableDocumentLifecycle<Note>? lifecycle = null;
        lifecycle = Lifecycle(write: (destination, text) =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            reloadedDuringSave = lifecycle!.Reload(destination);
            AdpFileWriter.Save(destination, text);
        });
        var note = lifecycle.GetOrLoad(path);
        note.Text = "edited";

        var saved = lifecycle.Save(path, note);

        Assert.False(saved.Failed);
        Assert.False(reloadedDuringSave);
        Assert.Same(note, lifecycle.Get(path));
    }

    [Fact]
    public void AReloadForItsOwnSave_ArrivingAfterTheSaveReturned_IsIgnored()
    {
        // The ordering the watcher actually produces: it reports a write after it lands, on its own
        // thread, so the notification for a save nearly always arrives once the save has returned.
        // A guard held only for the length of the write took that notification for an external
        // change, and every save was followed by a reload of the file it had just written.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle();
        var note = lifecycle.GetOrLoad(path);
        note.Text = "edited";
        lifecycle.Save(path, note);

        var reloaded = lifecycle.Reload(path);

        Assert.False(reloaded);
        Assert.Same(note, lifecycle.Get(path));
    }

    [Fact]
    public void AnExternalEditAfterItsOwnSave_IsTaken()
    {
        // The control for the test above: a guard that ignored every reload after a save would pass
        // that one and leave the store deaf to the next person who edits the file.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle();
        var note = lifecycle.GetOrLoad(path);
        note.Text = "edited";
        lifecycle.Save(path, note);
        File.WriteAllText(path, "changed elsewhere");

        var reloaded = lifecycle.Reload(path);

        Assert.True(reloaded);
        Assert.Equal("changed elsewhere", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AnExternalEditThatRestoresItsOwnSave_IsTaken()
    {
        // What the guard remembers is dropped the first time the file differs, so the text of an old
        // save cannot mask a later external change back to it while the cache holds something else.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle();
        var note = lifecycle.GetOrLoad(path);
        note.Text = "edited";
        lifecycle.Save(path, note);
        File.WriteAllText(path, "changed elsewhere");
        Assert.True(lifecycle.Reload(path));
        File.WriteAllText(path, "edited");

        var reloaded = lifecycle.Reload(path);

        Assert.True(reloaded);
        Assert.Equal("edited", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void AReloadWithNoSaveInFlight_IsTaken()
    {
        // The control for the test above: a Reload that ignored everything would pass that one.
        var path = Write("plan.note", "first");
        var lifecycle = Lifecycle();
        lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "second");

        var reloaded = lifecycle.Reload(path);

        Assert.True(reloaded);
        Assert.Equal("second", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void ARefusedWrite_IsReported_AndTheEditStaysInMemory()
    {
        // R3.2 and R3.4: told rather than thrown at, and the edit kept so a retry saves it.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle(write: (_, _) => throw new IOException("The process cannot access the file."));
        var note = lifecycle.GetOrLoad(path);
        note.Text = "edited";

        var saved = lifecycle.Save(path, note);

        Assert.True(saved.Failed);
        Assert.Contains("could not be written", saved.Error, StringComparison.Ordinal);
        Assert.Equal("before", File.ReadAllText(path));
        Assert.Same(note, lifecycle.Get(path));
    }

    [Fact]
    public void ARefusedWrite_ReleasesTheSelfWriteMark()
    {
        // A failed save that left its mark behind would make the store deaf to every later external
        // change of that file - the reload would be taken for its own write forever.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle(write: (_, _) => throw new IOException("refused"));
        var note = lifecycle.GetOrLoad(path);
        lifecycle.Save(path, note);
        File.WriteAllText(path, "changed elsewhere");

        var reloaded = lifecycle.Reload(path);

        Assert.True(reloaded);
        Assert.Equal("changed elsewhere", lifecycle.GetOrLoad(path).Text);
    }

    [Fact]
    public void ASaveThatSucceeds_WritesTheFile_AndReportsOk()
    {
        // The other direction: without it, a Save that always failed would pass the refusal tests.
        var path = Write("plan.note", "before");
        var lifecycle = Lifecycle();
        var note = lifecycle.GetOrLoad(path);
        note.Text = "after";

        var saved = lifecycle.Save(path, note);

        Assert.False(saved.Failed);
        Assert.Equal("", saved.Error);
        Assert.Equal("after", File.ReadAllText(path));
    }

    [Fact]
    public void ASaveIntoAFolderNotThereYet_CreatesIt()
    {
        // A freshly registered diagram's body can sit in a folder nobody has made yet, and the first
        // save is what creates it. Seven stores each did this themselves before task 6 moved it here.
        var path = IoPath.Combine(_folder, "not", "there", "yet", "plan.note");
        var lifecycle = Lifecycle();
        var note = lifecycle.GetOrLoad(path);
        note.Text = "first";

        var saved = lifecycle.Save(path, note);

        Assert.False(saved.Failed, saved.Error);
        Assert.Equal("first", File.ReadAllText(path));
    }

    [Fact]
    public void AnEditSavedAfterAnotherProgramWrote_KeepsBothChanges()
    {
        // agent-activity-diagram R8.6. The command read the document, another program wrote the file,
        // and the change notice has not arrived - so the cache still holds what the command read. The
        // plain save would write "one\n" plus the edit and the other program's line would be gone.
        var path = Write("plan.note", "one\n");
        var lifecycle = Lifecycle();
        var basis = lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "one\ntwo\n");

        var saved = lifecycle.Save(path, basis, note => DocumentEdit<Note>.Applied(new Note(note.Text + "mine\n")));

        Assert.False(saved.Failed, saved.Error);
        Assert.Equal("one\ntwo\nmine\n", File.ReadAllText(path));
    }

    [Fact]
    public void AnEditSavedWithNothingChangedOnDisk_IsAppliedToTheDocumentTheCallerRead()
    {
        // The other half: an unchanged file is not parsed again, so the edit sees the caller's own
        // document - the same reference - and a store pays nothing for the comparison but one read.
        var path = Write("plan.note", "one\n");
        var lifecycle = Lifecycle();
        var basis = lifecycle.GetOrLoad(path);
        Note? handed = null;

        var saved = lifecycle.Save(path, basis, note =>
        {
            handed = note;
            return DocumentEdit<Note>.Applied(new Note(note.Text + "mine\n"));
        });

        Assert.False(saved.Failed, saved.Error);
        Assert.Same(basis, handed);
        Assert.Equal("one\nmine\n", File.ReadAllText(path));
    }

    [Fact]
    public void AnEditThatNoLongerApplies_IsRefused_AndNothingIsWritten()
    {
        // The other program removed what the edit was for. The edit says so against the fresh
        // document, the user is told, and the file keeps the other program's text untouched.
        var path = Write("plan.note", "one\n");
        var lifecycle = Lifecycle();
        var basis = lifecycle.GetOrLoad(path);
        File.WriteAllText(path, "two\n");

        var saved = lifecycle.Save(path, basis, note => note.Text.Contains("one", StringComparison.Ordinal)
            ? DocumentEdit<Note>.Applied(new Note(note.Text + "mine\n"))
            : DocumentEdit<Note>.Refused("The entry this edit was for is no longer in the file."));

        Assert.True(saved.Failed);
        Assert.Equal("The entry this edit was for is no longer in the file.", saved.Error);
        Assert.Equal("two\n", File.ReadAllText(path));
        Assert.Same(basis, lifecycle.Get(path));
    }

    private static WritableDocumentLifecycle<Note> Lifecycle(Action<string, string>? write = null) =>
        new((_, text) => new Note(text), note => note.Text, unavailable: null, write ?? AdpFileWriter.Save);

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
