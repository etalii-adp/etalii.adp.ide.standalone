using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// A reload that arrives between a command's edit and its save must not discard the edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window is in <c>Save</c>'s shape, and mindmap's signature hides it.</b> Its
/// <c>Save(bodyPath, change)</c> looks loaded, because it takes a second argument - but the
/// argument is the change to ANNOUNCE, not the document to write. The document is looked up in
/// the cache again, and <c>Reload</c> assigns <c>_documents[bodyPath] = Load(bodyPath)</c>, so a
/// reload landing between a command's edit and its save means the document the command edited is
/// no longer the document that gets written. The save then persists the re-read file and reports
/// success.
/// </para>
/// <para>
/// <b>Why that is worse than a refused save.</b> The command reports success, so its inverse goes
/// onto the undo stack for a change that never reached the disk - and mindmap's inverses carry
/// state, so restoring a subtree that was never removed is the failure this produces.
/// </para>
/// </remarks>
public class MindmapDocumentStoreLostEditTests : IDisposable
{
    private const string Map = """
        <map version="freeplane 1.11.5">
          <node TEXT="Design" ID="ID_1"/>
        </map>
        """;

    private readonly TestFolder _testFolder = TestFolder.Create("adp-mindmap-lost-edit");
    private readonly string _root;

    private readonly MindmapDocumentStore _store = new(new FileWriter());

    public MindmapDocumentStoreLostEditTests() => _root = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASaveAfterAReload_WritesTheEditRatherThanTheFileItJustReread()
    {
        // Arrange: a map loaded and edited in memory, exactly as a mindmap command does it -
        // GetOrLoad, edit the document, then ask the store to save.
        var bodyPath = IoPath.Combine(_root, "design.mm");
        await File.WriteAllTextAsync(bodyPath, Map, TestContext.Current.CancellationToken);
        var document = await _store.GetOrLoadAsync(bodyPath, TestContext.Current.CancellationToken);
        document.SetText(document.Find("ID_1")!, "Design, renamed");

        // The edit is in memory and nowhere else yet, which is this test's precondition.
        Assert.Contains("Design, renamed", document.ToText(), StringComparison.Ordinal);

        // Act: the notification for an EARLIER write of this same file lands now. The store's own
        // self-write guard is already cleared, so this is exactly what the watcher does with it.
        await _store.ReloadAsync(bodyPath, TestContext.Current.CancellationToken);
        var saved = await _store.SaveAsync(bodyPath, document, new MindmapNodeUpdated("ID_1"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(saved.Failed);
        var written = await File.ReadAllTextAsync(bodyPath, TestContext.Current.CancellationToken);

        // The save reported success, so the edit it reported success for has to be in the file.
        Assert.Contains("Design, renamed", written, StringComparison.Ordinal);
    }
}
