using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The bytes a dependency graph holds after link gestures (etalii.adp spec 092, T024, SC-003): a linked node added from the lookup
/// where the canvas was clicked, and a dependency drawn onto it, saved and read again. <see cref="Expected"/> is what the desktop
/// host writes for the same gestures on the same document, the new ids aside, which each host draws for itself.
/// </summary>
public sealed partial class DependencyGraphLinkGestureBytesTests : IDisposable
{
    private const string Plain = "dependencies: 1\nid: s2v5b9hu\nelements:\n  - id: pay\n    label: Payments\n    x: 0\n    row: 0\n";

    /// <summary>The document after the gestures, each new id written as <c>{node}</c> or <c>{dependency}</c>.</summary>
    private const string Expected = Plain + "  - id: {node}\n    link: $p4r8t1zc@ideas.mm#ID_1=Shop relaunch\n    x: 240\n    row: 3\nrelations:\n  - id: {dependency}\n    from: pay\n    to: {node}\n";

    private readonly TestFolder _folder = TestFolder.Create("adp-dgr-link-gestures");
    private readonly DependencyGraphDocumentStore _store = new(new FileWriter());
    private readonly IHistoryStackStore _historyStacks;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public DependencyGraphLinkGestureBytesTests()
    {
        _historyStacks = TestHistory.For(s => { s.AddDependencyGraph(); s.AddSingleton<IDependencyGraphDocumentStore>(_store); });
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ALinkedNodeAndADependencyOntoIt_AreWrittenAsTheDesktopHostWritesThem_AndReadBackLinked()
    {
        // Arrange.
        var path = IoPath.Combine(_folder.Path, "services.dgr");
        await File.WriteAllTextAsync(path, Plain, TestContext.Current.CancellationToken);
        var actions = new DependencyGraphContextActionProvider(_historyStacks, _store, new DependencyGraphOpeningIndex(Workspace()));

        // Act: add the linked node, draw a dependency from Payments onto it, and read the document again from its bytes.
        var added = await actions.CommitAsync(Target(path, GestureIds.RowPlacement(240, 3)), DependencyGraphContextActionProvider.AddLinkedActionId, "$p4r8t1zc@ideas.mm#ID_1=Shop relaunch", "", TestContext.Current.CancellationToken);
        var node = NodeId().Match(await ReadAsync(path)).Groups[1].Value;
        var connected = await actions.ExecuteAsync(Target(path, GestureIds.Relation("pay", node)), DependencyGraphContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);
        var written = await ReadAsync(path);
        _store.Forget(path);
        var reopened = (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model;

        // Assert.
        Assert.True(added.Completed, added.Error);
        Assert.IsType<ContextExecutionCompleted>(connected);
        var dependency = Assert.Single(reopened.Relations);
        Assert.Equal(Expected.Replace("{node}", node, StringComparison.Ordinal).Replace("{dependency}", dependency.Id, StringComparison.Ordinal), written);
        Assert.Equal(("pay", node), (dependency.From, dependency.To));
    }

    [GeneratedRegex("  - id: ([^\\n]+)\\n    link: ")]
    private static partial Regex NodeId();

    private ContextTarget Target(string path, string elementId) => ContextTarget.ForElement(path, elementId, ProjectRoot.From(_folder.Path), _watchId);

    private static async Task<string> ReadAsync(string path) => (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

    private LinkWorkspace Workspace() => new(_folder.Path.Replace('\\', '/'), [
        new IndexedDocument(_folder.Path.Replace('\\', '/') + "/ideas.mm", "p4r8t1zc", "text", [new IndexedElement("ID_1", [new KeyValuePair<string, string>("text", "Shop relaunch")])], []),
    ]);
}
