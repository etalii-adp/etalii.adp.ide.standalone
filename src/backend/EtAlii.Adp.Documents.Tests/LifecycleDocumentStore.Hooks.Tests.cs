using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// What a module keeps beside a document hears from the shared store when the document is saved,
/// reloaded or deleted, before the sessions do, and a warning of its own reaches the save's result
/// (code-structure FR-029).
/// </summary>
public sealed class LifecycleDocumentStoreHooksTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-lifecycle-hooks-" + Guid.NewGuid().ToString("N"));

    public LifecycleDocumentStoreHooksTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestFolder.TryDelete(_folder);

    private string Write(string text)
    {
        var path = IoPath.Combine(_folder, "notes.txt");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task ASave_CarriesTheWarningOfWhatIsKeptBesideIt_AndTellsItBeforeTheSessions()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("the index beside it was not written");
        var document = (await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document;
        document.Replace(new LineRange(0, 0), ["after"]);

        // Act.
        var saved = await store.SaveAsync(path, document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(saved.Failed, saved.Error);
        Assert.Equal("the index beside it was not written", saved.Warning);
        Assert.Equal(["saved after\n", "changed"], store.Heard);
    }

    [Fact]
    public async Task AnEditSavedAgainstTheFile_CarriesTheWarningToo()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("the index beside it was not written");
        var basis = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var saved = (await store.SaveAsync(path, basis, body =>
        {
            body.Replace(new LineRange(0, 0), ["after"]);
            return "";
        }, TestContext.Current.CancellationToken));

        // Assert.
        Assert.False(saved.Result.Failed, saved.Result.Error);
        Assert.Equal("the index beside it was not written", saved.Result.Warning);
        Assert.Equal(["saved after\n", "changed"], store.Heard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatInstallsADocument_AndADeletion_TellWhatIsKeptBesideIt_BeforeTheSessions(bool asynchronously)
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("");
        await GetOrLoad(store, path, asynchronously);
        await File.WriteAllTextAsync(path, "written elsewhere\n", TestContext.Current.CancellationToken);

        // Act.
        await Reload(store, path, asynchronously);
        File.Delete(path);
        await BodyDeleted(store, path, asynchronously);

        // Assert.
        Assert.Equal(["installed written elsewhere\n", "changed", "installed ", "changed"], store.Heard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReloadThatKeepsTheLastGoodDocument_TellsNothingBesideIt(bool asynchronously)
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("");
        await GetOrLoad(store, path, asynchronously);
        File.Delete(path);

        // Act: a reload that finds no file is far more often a publish in flight than a deletion.
        await Reload(store, path, asynchronously);

        // Assert.
        Assert.Empty(store.Heard);
    }

    // The synchronous reads and the asynchronous ones answer alike until the synchronous ones go
    // (code-structure T283), so these run through both.
    private static async Task GetOrLoad(KeepingStore store, string path, bool asynchronously)
    {
        if (asynchronously)
        {
            await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
            return;
        }

        await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
    }

    private static async Task Reload(KeepingStore store, string path, bool asynchronously)
    {
        if (asynchronously)
        {
            await store.ReloadAsync(path, TestContext.Current.CancellationToken);
            return;
        }

        await store.ReloadAsync(path, TestContext.Current.CancellationToken);
    }

    private static async Task BodyDeleted(KeepingStore store, string path, bool asynchronously)
    {
        if (asynchronously)
        {
            await store.BodyDeletedAsync(path, TestContext.Current.CancellationToken);
            return;
        }

        await store.BodyDeletedAsync(path, TestContext.Current.CancellationToken);
    }

    /// <summary>A store over plain lines that records what it is told, and warns of every save.</summary>
    private sealed class KeepingStore : LifecycleDocumentStore<LineDocument, string>
    {
        private readonly string _warning;

        public KeepingStore(string warning)
            : base(new FileWriter())
        {
            _warning = warning;
            Changed += (_, _) => Heard.Add("changed");
        }

        public List<string> Heard { get; } = [];

        protected override LineDocument Read(string text) => LineDocument.Parse(text);

        protected override string TextOf(LineDocument body) => body.Text;

        protected override ParseOutcome<string> Parse(LineDocument body) => new(body.Text, []);

        protected override Task<string> SavedAsync(string path, DocumentEntry<LineDocument, string> entry, CancellationToken cancellationToken)
        {
            Heard.Add("saved " + entry.Model);
            return Task.FromResult(_warning);
        }

        protected override void Installed(string path) => Heard.Add("installed " + GetOrLoad(path).Model);
    }
}
