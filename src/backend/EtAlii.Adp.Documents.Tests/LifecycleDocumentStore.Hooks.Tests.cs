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
    private readonly TestFolder _testFolder = TestFolder.Create("adp-lifecycle-hooks");
    private readonly string _folder;

    public LifecycleDocumentStoreHooksTests() => _folder = _testFolder.Path;

    public void Dispose() => _testFolder.Dispose();

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

    [Fact]
    public async Task AReloadThatInstallsADocument_AndADeletion_TellWhatIsKeptBesideIt_BeforeTheSessions()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("");
        await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "written elsewhere\n", TestContext.Current.CancellationToken);

        // Act.
        await store.ReloadAsync(path, TestContext.Current.CancellationToken);
        File.Delete(path);
        await store.BodyDeletedAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(["installed written elsewhere\n", "changed", "installed ", "changed"], store.Heard);
    }

    [Fact]
    public async Task AReloadThatKeepsTheLastGoodDocument_TellsNothingBesideIt()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("");
        await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        File.Delete(path);

        // Act: a reload that finds no file is far more often a publish in flight than a deletion.
        await store.ReloadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(store.Heard);
    }

    [Fact]
    public async Task EveryBodyAnEntryHolds_IsToldItsPath_WhetherOpenedSavedEditedReloadedOrMissing()
    {
        // Arrange.
        var path = Write("before\n");
        var missing = IoPath.Combine(_folder, "missing.txt");
        var store = new LocatingStore();
        var token = TestContext.Current.CancellationToken;

        // Act.
        var opened = (await store.GetOrLoadAsync(path, token)).Model;
        await store.SaveAsync(path, LocatedLines.Parse("saved\n"), token);
        var saved = store.Get(path)!.Model;
        await store.SaveAsync(path, store.Get(path)!, _ => "", token);
        var edited = store.Get(path)!.Model;
        await File.WriteAllTextAsync(path, "written elsewhere\n", token);
        await store.ReloadAsync(path, token);
        var reloaded = store.Get(path)!.Model;
        var absent = (await store.GetOrLoadAsync(missing, token)).Model;

        // Assert.
        Assert.Equal(["notes.txt", "notes.txt", "notes.txt", "notes.txt", "missing.txt"], [opened, saved, edited, reloaded, absent]);
    }

    /// <summary>A store whose model is the name of the file its body was told it is in, or <c>nowhere</c>.</summary>
    private sealed class LocatingStore() : LifecycleDocumentStore<LocatedLines, string>(new FileWriter())
    {
        protected override LocatedLines Read(string text) => LocatedLines.Parse(text);

        protected override LocatedLines Locate(string path, LocatedLines body) => body.At(IoPath.GetFileName(path));

        protected override string TextOf(LocatedLines body) => body.Text;

        protected override ParseOutcome<string> Parse(LocatedLines body) => new(body.Name, []);
    }

    /// <summary>A text, with the name of the file it was told it is in.</summary>
    private sealed record LocatedLines(string Text, string Name) : ITextBody<LocatedLines>
    {
        public static LocatedLines Parse(string text) => new(text, "nowhere");

        public LocatedLines At(string name) => this with { Name = name };
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

        protected override void Installed(string path) => Heard.Add("installed " + Get(path)!.Model);
    }
}
