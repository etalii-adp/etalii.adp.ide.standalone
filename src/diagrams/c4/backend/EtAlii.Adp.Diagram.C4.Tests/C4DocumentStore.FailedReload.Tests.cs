using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// A reload that cannot read the body must not leave the store holding something a later save
/// would write over the real file.
/// </summary>
/// <remarks>
/// <c>C4DocumentStore.Load</c> treats a read failure as an empty document, and <c>Save</c> writes
/// whatever the entry holds - so the next save after a failed reload could replace a real model
/// with an empty one. The read failure is made deterministic here with a handle that shares
/// nothing, instead of racing a publish for it.
/// </remarks>
public class C4DocumentStoreFailedReloadTests : IDisposable
{
    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking."
                u -> s "Uses"
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.FailedReload", Guid.NewGuid().ToString("N"));

    public C4DocumentStoreFailedReloadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ASaveAfterAReloadThatCouldNotRead_DoesNotWriteAnEmptyModelOverTheRealFile()
    {
        // Arrange: a real model, loaded.
        var body = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(body, Model);
        var store = new C4DocumentStore();
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);

        // Arrange, continued: a reload that cannot read the body.
        using (new FileStream(body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Reload(body);
        }

        // Assert, first: the store still holds the model - the failed read did not replace it.
        // Asserted on its own because the save refusal would protect the file even without it.
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);

        // Act: whatever triggers the next save, once the file is readable again.
        store.Save(body, store.GetOrLoad(body));

        // Assert: the real model is still on disk.
        var onDisk = File.ReadAllText(body);
        Assert.Contains("Customer", onDisk, StringComparison.Ordinal);
        Assert.Contains("Banking", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public void ASaveOfAModelThatCouldNotBeReadEvenOnce_RefusesAndLeavesTheFileAlone()
    {
        // The case keeping the last good model cannot cover: the very first read fails, so there is
        // nothing good to keep and the store opens the model as empty. Writing that would put an
        // empty model over the only copy of the real one.
        var body = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(body, Model);
        var store = new C4DocumentStore();

        using (new FileStream(body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(store.WorkspaceOf(body).Elements); // the arrangement: the first read failed
        }

        var answer = store.Save(body, store.GetOrLoad(body));

        Assert.NotEqual("", answer);
        Assert.Equal(Model, File.ReadAllText(body));
    }

    [Fact]
    public void ABodyThatIsReallyDeleted_EndsAsAnEmptyModel_NotTheLastOneKeptAlive()
    {
        // The other half of keeping the last good model, and the reason it cannot be the whole
        // rule. DiagramDocumentReloadBridge documents that "a body that is gone is an empty
        // diagram, not the last one kept alive" - a store that kept the last model through a real
        // delete would draw a diagram forever that no longer exists, the same bug pointed the
        // other way. A missing body is confirmed gone - by the watcher's Deleted event, which a
        // publish in flight never raises - before it is treated as gone.
        var body = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(body, Model);
        var store = new C4DocumentStore();
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);

        File.Delete(body);
        store.BodyDeleted(body);

        Assert.Empty(store.WorkspaceOf(body).Elements);
    }

    [Fact]
    public async Task ABodyDeletedUnderAnOpenModel_EndsEmpty_ThroughTheWatcher()
    {
        // The same rule end to end, through the real bridge and reloader: the delete the user makes
        // has to reach the store as a deletion. A bridge that still reloads on it leaves the store
        // keeping the last good model, and the deleted diagram stays drawn.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore();
        var origin = new DiagramOrigin("test", "c4");
        using var bridge = new DiagramDocumentReloadBridge([new C4DocumentReloader(origin, store)]);
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);
        bridge.Track(_root, body, registrationPath: null, origin);

        File.Delete(body);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (store.WorkspaceOf(body).Elements.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Empty(store.WorkspaceOf(body).Elements);
    }

    [Fact]
    public async Task ExternalSavesOfAnUnchangedModel_NeverEmptyIt_ThroughTheWatcher()
    {
        // The must-not-catch half end to end: every external save renames the body away for an
        // instant, and none of those may reach the store as a deletion. Measured on the store's own
        // change event, so an empty model installed and then healed still counts.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore();
        var origin = new DiagramOrigin("test", "c4");
        using var bridge = new DiagramDocumentReloadBridge([new C4DocumentReloader(origin, store)]);
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);
        var changes = 0;
        var emptied = 0;
        store.Changed += (_, args) =>
        {
            Interlocked.Increment(ref changes);
            if (args.Workspace.Elements.Count == 0)
            {
                Interlocked.Increment(ref emptied);
            }
        };
        bridge.Track(_root, body, registrationPath: null, origin);

        for (var i = 0; i < 50; i++)
        {
            Documents.AdpFileWriter.Save(body, Model);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(changes > 0, "The arrangement failed: no external save reached the store.");
        Assert.True(emptied == 0, $"{emptied} of {changes} changes after 50 external saves emptied the model.");
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);
    }

    [Fact]
    public void ANewBodyThatDoesNotExistYet_StillOpensEmptyAndSaves()
    {
        // The must-not-catch half: a missing body on a FIRST load is a new document, as it always
        // was - refusing to save it would break creating a diagram.
        var body = IoPath.Combine(_root, "new.dsl");
        var store = new C4DocumentStore();

        Assert.Empty(store.WorkspaceOf(body).Elements);
        var answer = store.Save(body, store.GetOrLoad(body));

        Assert.Equal("", answer);
        Assert.True(File.Exists(body), "A new, empty model was not saved.");
    }

    [Fact]
    public async Task ReloadsRacingAnExternalPublishOfTheSameModel_NeverLoseIt()
    {
        // The measured race, as a guard. An external writer republishes an UNCHANGED model while the
        // store reloads it, which is what a text editor saving over the file does. On develop 765 of
        // 3000 reloads installed an empty workspace - and with removals pushed to the canvas, that
        // blanked it. A publish either lands whole or makes the read fail, and a failed read now
        // keeps the last good model, so zero is the only passing answer rather than a lucky one.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        // The retry's wait taken away, and only here: every reload that finds the body renamed away
        // mid-publish now waits between attempts, and at 50 ms this test went from 0.9 s to 15.3 s
        // (2026-09-25). What it guards is that the model is never lost, which keep-last-good decides,
        // not how long a retry waits; the store's own attempts are kept.
        var store = new C4DocumentStore(Documents.SharedDocumentReader.ReadAllText, C4DocumentStore.DefaultReadAttempts, TimeSpan.Zero);
        Assert.NotEmpty(store.WorkspaceOf(body).Elements);

        using var stop = new CancellationTokenSource();
        var publishes = 0;
        var writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    Documents.AdpFileWriter.Save(body, Model);
                    Interlocked.Increment(ref publishes);
                }
                catch (IOException)
                {
                    // A refused publish is not what this guard measures.
                }
            }
        }, TestContext.Current.CancellationToken);

        const int reloads = 3000;
        var lost = 0;
        try
        {
            for (var i = 0; i < reloads; i++)
            {
                store.Reload(body);
                if (store.WorkspaceOf(body).Elements.Count == 0)
                {
                    lost++;
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            await writer;
        }

        Assert.True(publishes > 0, "The arrangement failed: the external writer never published, so nothing raced.");
        Assert.True(lost == 0, $"{lost} of {reloads} reloads racing {publishes} external publishes lost the model.");
    }
}
