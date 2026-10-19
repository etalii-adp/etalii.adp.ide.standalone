using EtAlii.Adp.Files;
using EtAlii.Adp.Workspace;
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
    private readonly FileWriter _writer = new();

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
    public async Task ASaveAfterAReloadThatCouldNotRead_DoesNotWriteAnEmptyModelOverTheRealFile()
    {
        // Arrange: a real model, loaded.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore(new FileWriter());
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);

        // Arrange, continued: a reload that cannot read the body.
        await using (new FileStream(body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await store.ReloadAsync(body, TestContext.Current.CancellationToken);
        }

        // Assert, first: the store still holds the model - the failed read did not replace it.
        // Asserted on its own because the save refusal would protect the file even without it.
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);

        // Act: whatever triggers the next save, once the file is readable again.
        await store.SaveAsync(body, (await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken);

        // Assert: the real model is still on disk.
        var onDisk = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Contains("Customer", onDisk, StringComparison.Ordinal);
        Assert.Contains("Banking", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASaveOfAModelThatCouldNotBeReadEvenOnce_RefusesAndLeavesTheFileAlone()
    {
        // The case keeping the last good model cannot cover: the very first read fails, so there is
        // nothing good to keep and the store opens the model as empty. Writing that would put an
        // empty model over the only copy of the real one.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore(new FileWriter());

        await using (new FileStream(body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements); // the arrangement: the first read failed
        }

        var answer = await store.SaveAsync(body, (await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken);

        Assert.NotEqual("", answer.Error);
        Assert.Equal(Model, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABodyThatIsReallyDeleted_EndsAsAnEmptyModel_NotTheLastOneKeptAlive()
    {
        // The other half of keeping the last good model, and the reason it cannot be the whole
        // rule. DiagramDocumentReloadBridge documents that "a body that is gone is an empty
        // diagram, not the last one kept alive" - a store that kept the last model through a real
        // delete would draw a diagram forever that no longer exists, the same bug pointed the
        // other way. A missing body is confirmed gone - by the watcher's Deleted event, which a
        // publish in flight never raises - before it is treated as gone.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore(new FileWriter());
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);

        File.Delete(body);
        await store.BodyDeletedAsync(body, TestContext.Current.CancellationToken);

        Assert.Empty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);
    }

    [Fact]
    public async Task ABodyDeletedUnderAnOpenModel_EndsEmpty_ThroughTheWatcher()
    {
        // The same rule end to end, through the real bridge and reloader: the delete the user makes
        // has to reach the store as a deletion. A bridge that still reloads on it leaves the store
        // keeping the last good model, and the deleted diagram stays drawn.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore(new FileWriter());
        var origin = new ToolOrigin("test", "c4");
        using var events = new ProjectFileEvents();
        using var bridge = new DiagramDocumentReloadBridge([new DiagramDocumentReloader<IC4DocumentStore>(origin, store)], events);
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);
        bridge.Track(ProjectRoot.From(_root), body, registrationPath: null, origin);

        File.Delete(body);

        Assert.True(
            await Eventually.TrueAsync(() => store.Get(body)?.Model.Elements.Count == 0, TestContext.Current.CancellationToken),
            "The deletion did not empty the model within the timeout.");
    }

    [Fact]
    public async Task ExternalSavesOfAnUnchangedModel_NeverEmptyIt_ThroughTheWatcher()
    {
        // The must-not-catch half end to end: every external save renames the body away for an
        // instant, and none of those may reach the store as a deletion. Measured on the store's own
        // change event, so an empty model installed and then healed still counts.
        var body = IoPath.Combine(_root, "model.dsl");
        await File.WriteAllTextAsync(body, Model, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore(new FileWriter());
        var origin = new ToolOrigin("test", "c4");
        using var events = new ProjectFileEvents();
        using var bridge = new DiagramDocumentReloadBridge([new DiagramDocumentReloader<IC4DocumentStore>(origin, store)], events);
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);
        var changes = 0;
        var emptied = 0;
        store.Changed += (_, args) =>
        {
            Interlocked.Increment(ref changes);
            if (store.Get(args.Path)?.Model.Elements.Count == 0)
            {
                Interlocked.Increment(ref emptied);
            }
        };
        bridge.Track(ProjectRoot.From(_root), body, registrationPath: null, origin);

        for (var i = 0; i < 50; i++)
        {
            await _writer.SaveAsync(body, Model, TestContext.Current.CancellationToken);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(
            await Eventually.TrueAsync(() => Volatile.Read(ref changes) > 0, TestContext.Current.CancellationToken),
            "The arrangement failed: no external save reached the store.");
        Assert.False(
            await Eventually.TrueAsync(() => Volatile.Read(ref emptied) > 0, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(1)),
            $"{emptied} of {changes} changes after 50 external saves emptied the model.");
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);
    }

    [Fact]
    public async Task ANewBodyThatDoesNotExistYet_StillOpensEmptyAndSaves()
    {
        // The must-not-catch half: a missing body on a FIRST load is a new document, as it always
        // was - refusing to save it would break creating a diagram.
        var body = IoPath.Combine(_root, "new.dsl");
        var store = new C4DocumentStore(new FileWriter());

        Assert.Empty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Elements);
        var answer = await store.SaveAsync(body, (await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken);

        Assert.Equal("", answer.Error);
        Assert.True(File.Exists(body), "A new, empty model was not saved.");
    }
}
