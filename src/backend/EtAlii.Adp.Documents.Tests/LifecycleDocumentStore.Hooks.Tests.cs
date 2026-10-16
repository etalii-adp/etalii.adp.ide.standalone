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
    public void ASave_CarriesTheWarningOfWhatIsKeptBesideIt_AndTellsItBeforeTheSessions()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("the index beside it was not written");
        var document = store.GetOrLoad(path).Document;
        document.Replace(new LineRange(0, 0), ["after"]);

        // Act.
        var saved = store.Save(path, document);

        // Assert.
        Assert.False(saved.Failed, saved.Error);
        Assert.Equal("the index beside it was not written", saved.Warning);
        Assert.Equal(["saved after\n", "changed"], store.Heard);
    }

    [Fact]
    public void AnEditSavedAgainstTheFile_CarriesTheWarningToo()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("the index beside it was not written");
        var basis = store.GetOrLoad(path);

        // Act.
        var saved = store.Save(path, basis, body =>
        {
            body.Replace(new LineRange(0, 0), ["after"]);
            return "";
        });

        // Assert.
        Assert.False(saved.Result.Failed, saved.Result.Error);
        Assert.Equal("the index beside it was not written", saved.Result.Warning);
        Assert.Equal(["saved after\n", "changed"], store.Heard);
    }

    [Fact]
    public void AReloadThatInstallsADocument_AndADeletion_TellWhatIsKeptBesideIt_BeforeTheSessions()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("");
        store.GetOrLoad(path);
        File.WriteAllText(path, "written elsewhere\n");

        // Act.
        store.Reload(path);
        File.Delete(path);
        store.BodyDeleted(path);

        // Assert.
        Assert.Equal(["installed written elsewhere\n", "changed", "installed ", "changed"], store.Heard);
    }

    [Fact]
    public void AReloadThatKeepsTheLastGoodDocument_TellsNothingBesideIt()
    {
        // Arrange.
        var path = Write("before\n");
        var store = new KeepingStore("");
        store.GetOrLoad(path);
        File.Delete(path);

        // Act: a reload that finds no file is far more often a publish in flight than a deletion.
        store.Reload(path);

        // Assert.
        Assert.Empty(store.Heard);
    }

    /// <summary>A store over plain lines that records what it is told, and warns of every save.</summary>
    private sealed class KeepingStore : LifecycleDocumentStore<LineDocument, string>
    {
        private readonly string _warning;

        public KeepingStore(string warning)
        {
            _warning = warning;
            Changed += (_, _) => Heard.Add("changed");
        }

        public List<string> Heard { get; } = [];

        protected override LineDocument Read(string text) => LineDocument.Parse(text);

        protected override string TextOf(LineDocument body) => body.Text;

        protected override ParseOutcome<string> Parse(LineDocument body) => new(body.Text, []);

        protected override string Saved(string path, DocumentEntry<LineDocument, string> entry)
        {
            Heard.Add("saved " + entry.Model);
            return _warning;
        }

        protected override void Installed(string path) => Heard.Add("installed " + GetOrLoad(path).Model);
    }
}
