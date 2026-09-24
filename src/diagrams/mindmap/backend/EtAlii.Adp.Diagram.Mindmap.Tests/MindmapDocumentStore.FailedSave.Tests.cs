using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// A save the disk refuses is reported, and the edit stays in memory to be retried
/// (backend-centralization R3.2, R3.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this changed, because the behaviour before it was user-visible.</b> Mindmap's
/// <c>Save</c> returned <c>void</c> and let <c>AdpFileWriter</c>'s exception escape - out of the
/// store, out of the command handler, and out to the caller as an exception rather than a sentence.
/// A user whose map was held open by another program got nothing they could act on, and no caller
/// could read a failure, because there was no result to read. Every other writable store already
/// returned a message; mindmap was the one that threw.
/// </para>
/// <para>
/// <b>The lock is the established way to refuse a write here</b> - the same
/// <c>FileShare.Read</c> holder <c>C4Commands.Tests</c> uses, which makes the writer's
/// temp-then-replace fail rather than simulating a failure with a stub. Windows-only by nature:
/// <c>FileShare</c> is a mandatory lock only there.
/// </para>
/// </remarks>
public class MindmapDocumentStoreFailedSaveTests : IDisposable
{
    private const string Map = """
        <map version="freeplane 1.11.5">
          <node TEXT="Design" ID="ID_1"/>
        </map>
        """;

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "adp-mindmap-failed-save-" + Guid.NewGuid().ToString("N"));
    private readonly MindmapDocumentStore _store = new();

    public MindmapDocumentStoreFailedSaveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestFolder.TryDelete(_root);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ARefusedWrite_IsReported_AndTheEditIsStillThereToRetry()
    {
        // Arrange: a loaded map, an edit made in memory, and the file held open so the write fails.
        var bodyPath = IoPath.Combine(_root, "design.mm");
        File.WriteAllText(bodyPath, Map);
        var document = _store.GetOrLoad(bodyPath);
        var node = document.Find("ID_1")!;
        document.SetText(node, "Design, renamed");

        using var holder = new FileStream(bodyPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act.
        var saved = _store.Save(bodyPath, new MindmapNodeUpdated("ID_1"));

        // Assert: told, rather than thrown at. Reaching this line at all is the change - before it,
        // the writer's exception left the store and this test could not be written against a result.
        Assert.True(saved.Failed);
        Assert.Contains("could not be written", saved.Error, StringComparison.Ordinal);
        Assert.Equal("", saved.Warning);

        // ...and the edit is still in memory, so the user retries rather than retypes. THIS is the
        // half that matters to somebody using the app: the file on disk is untouched, and the
        // document ADP holds is the edited one.
        Assert.Equal("Design, renamed", _store.GetOrLoad(bodyPath).Find("ID_1")!.Text);
        Assert.Contains("TEXT=\"Design\"", File.ReadAllText(bodyPath), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteThatSucceeds_ReportsOk_AndLandsOnDisk()
    {
        // The other direction: without this, a Save that always reported failure would pass the test
        // above and break every edit in the product.
        var bodyPath = IoPath.Combine(_root, "design.mm");
        File.WriteAllText(bodyPath, Map);
        var document = _store.GetOrLoad(bodyPath);
        document.SetText(document.Find("ID_1")!, "Design, renamed");

        var saved = _store.Save(bodyPath, new MindmapNodeUpdated("ID_1"));

        Assert.False(saved.Failed);
        Assert.Equal("", saved.Error);
        Assert.Contains("Design, renamed", File.ReadAllText(bodyPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SavingAMapThatWasNeverLoaded_StillThrows()
    {
        // Deliberately unchanged, and stated so nobody "finishes the job" by converting it. Saving a
        // document this store never loaded is a programming error rather than an outcome a user can
        // act on - there is no edit to keep and nothing to retry.
        Assert.Throws<InvalidOperationException>(
            () => _store.Save(IoPath.Combine(_root, "never-opened.mm"), new MindmapNodeUpdated("ID_1")));
    }
}
