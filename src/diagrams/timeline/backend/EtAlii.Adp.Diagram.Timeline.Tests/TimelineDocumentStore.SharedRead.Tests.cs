using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The store's read side against the world's write side: a load must succeed while a writer
/// holds the file, per the sharing discipline SharedDocumentReader carries. Windows enforces
/// sharing, so this guard bites there; a load refused here used to become an empty document
/// pushed over a full one.
/// </summary>
public class TimelineDocumentStoreSharedReadTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("adp-timeline-store");
    private readonly string _workspace;

    public TimelineDocumentStoreSharedReadTests() => _workspace = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetOrLoad_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        const string text = "timeline: 1\nelements:\n  - id: aaa\n    label: Period\n    begin: 2026-01-05\n    end: 2026-02-13\n    row: 0\n";
        var path = IoPath.Combine(_workspace, "plan.tml");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        await using var editor = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var entry = await new TimelineDocumentStore(new FileWriter()).GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert: the document itself, not the empty fallback.
        Assert.True(entry.IsUsable);
        Assert.Equal(text, entry.Document.Text);
    }

    [Fact]
    public async Task GetOrLoad_ADocumentThatIsNotYaml_CarriesTheReasonAndTheLineItStoppedAt()
    {
        // Arrange: the corpus's one document that is not YAML, whose fourth line maps a key twice.
        var path = IoPath.Combine(_workspace, "unparseable.tml");
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "findings", "unparseable.tml"), path);

        // Act.
        var entry = await new TimelineDocumentStore(new FileWriter()).GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("line 4", entry.Unreadable, StringComparison.Ordinal);
    }
}
