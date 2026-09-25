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

    private static WritableDocumentLifecycle<Note> Lifecycle(Action<string, string>? write = null) =>
        new((_, text) => new Note(text), note => note.Text, unavailable: null, write ?? new Action<string, string>(AdpFileWriter.Save));

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
