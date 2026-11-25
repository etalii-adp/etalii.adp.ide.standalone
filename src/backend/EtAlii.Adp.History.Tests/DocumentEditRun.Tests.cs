using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.History.Tests;

/// <summary>The shared run of an edit, where what it does is not already a module's handler test (code-structure FR-033).</summary>
public sealed class DocumentEditRunTests
{
    private const string Path = "body.yml";

    [Fact]
    public async Task AnEditThatChangesNothing_SavesNothing_AndRecordsNoUndo()
    {
        // Arrange.
        var store = new CountingStore("name: kept\n");

        // Act.
        var result = await DocumentEditRun.RunAsync<CountingStore, LineDocument, string>(store, Path, new SampleEdit(), body => body.Text, (_, _) => EditOutcome.Unchanged, TestContext.Current.CancellationToken);

        // Assert: an undo step for an edit that did not happen appears to do nothing when reached.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Inverse);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task AnAppliedEdit_IsSaved_AndUndoneByTheWholeText()
    {
        // Arrange.
        var store = new CountingStore("name: kept\n");

        // Act.
        var result = await DocumentEditRun.RunAsync<CountingStore, LineDocument, string>(store, Path, new SampleEdit(), body => body.Text, (_, _) => EditOutcome.Applied, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, store.Saves);
        var restore = Assert.IsType<RestoreDocumentCommand<CountingStore>>(result.Inverse);
        Assert.Equal("name: kept\n", restore.Text);
    }

    [Fact]
    public async Task TheFirstEdit_WritesWhatIsPending_AndItsUndoTakesThatBackToo()
    {
        // Arrange: a document whose opening found changes it holds but has not written (etalii.adp spec 092, R6).
        var store = new CountingStore("name: kept\n", pending: "id: d0c1d2e3\nname: kept\n");

        // Act.
        var result = await DocumentEditRun.RunAsync<CountingStore, LineDocument, string>(store, Path, new SampleEdit(), body => body.Text, Append, TestContext.Current.CancellationToken);

        // Assert: the edit is made on what is pending, and undone to the file as it was read.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("id: d0c1d2e3\nname: kept\nnote: added\n", store.Saved);
        var restore = Assert.IsType<RestoreDocumentCommand<CountingStore>>(result.Inverse);
        Assert.Equal("name: kept\n", restore.Text);
    }

    [Fact]
    public async Task AnEditThatChangesNothing_WritesNothingPendingEither()
    {
        // Arrange.
        var store = new CountingStore("name: kept\n", pending: "id: d0c1d2e3\nname: kept\n");

        // Act.
        var result = await DocumentEditRun.RunAsync<CountingStore, LineDocument, string>(store, Path, new SampleEdit(), body => body.Text, (_, _) => EditOutcome.Unchanged, TestContext.Current.CancellationToken);

        // Assert: a document opened and left as it is is never written.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, store.Saves);
    }

    /// <summary>An edit that adds one line at the end.</summary>
    private static EditOutcome Append(LineDocument body, string model)
    {
        body.Insert(body.Lines.Count, ["note: added"]);
        return EditOutcome.Applied;
    }

    /// <summary>A store holding one document in memory, and what it holds pending; counting its saves and keeping the last.</summary>
    private sealed class CountingStore(string text, string? pending = null) : IDiagramDocumentStore<LineDocument, string>
    {
        public int Saves { get; private set; }

        public string? Saved { get; private set; }

        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public ValueTask<DocumentEntry<LineDocument, string>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => ValueTask.FromResult(Get(path));

        public DocumentEntry<LineDocument, string> Get(string path) => new(LineDocument.Parse(text), text) { Pending = pending };

        public Task<DocumentSaveResult> SaveAsync(string path, LineDocument document, CancellationToken cancellationToken)
        {
            Saves++;
            Saved = document.Text;
            return Task.FromResult(DocumentSaveResult.Ok);
        }

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Forget(string path)
        {
        }
    }

    private sealed record SampleEdit : ICommand;
}
