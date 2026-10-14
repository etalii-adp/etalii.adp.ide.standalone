using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.History.Tests;

/// <summary>The shared run of an edit, where what it does is not already a module's handler test (code-structure FR-033).</summary>
public sealed class DocumentEditRunTests
{
    private const string Path = "body.yml";

    [Fact]
    public void AnEditThatChangesNothing_SavesNothing_AndRecordsNoUndo()
    {
        // Arrange.
        var store = new CountingStore("name: kept\n");

        // Act.
        var result = DocumentEditRun.Run<CountingStore, LineDocument, string>(store, Path, new SampleEdit(), body => body.Text, (_, _) => EditOutcome.Unchanged);

        // Assert: an undo step for an edit that did not happen appears to do nothing when reached.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Inverse);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void AnAppliedEdit_IsSaved_AndUndoneByTheWholeText()
    {
        // Arrange.
        var store = new CountingStore("name: kept\n");

        // Act.
        var result = DocumentEditRun.Run<CountingStore, LineDocument, string>(store, Path, new SampleEdit(), body => body.Text, (_, _) => EditOutcome.Applied);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, store.Saves);
        var restore = Assert.IsType<RestoreDocumentCommand<CountingStore>>(result.Inverse);
        Assert.Equal("name: kept\n", restore.Text);
    }

    /// <summary>A store holding one document in memory, counting its saves.</summary>
    private sealed class CountingStore(string text) : IDiagramDocumentStore<LineDocument, string>
    {
        public int Saves { get; private set; }

        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public DocumentEntry<LineDocument, string> GetOrLoad(string path) => new(LineDocument.Parse(text), text);

        public DocumentSaveResult Save(string path, LineDocument document)
        {
            Saves++;
            return DocumentSaveResult.Ok;
        }

        public void Reload(string path)
        {
        }

        public void BodyDeleted(string path)
        {
        }

        public void Forget(string path)
        {
        }
    }

    private sealed record SampleEdit : ICommand;
}
