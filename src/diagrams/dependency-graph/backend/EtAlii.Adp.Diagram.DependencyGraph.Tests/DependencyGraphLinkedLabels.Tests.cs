using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// A linked node of an open graph shows its target as the workspace now holds it (DISL 4.12, FBL 17.1, etalii.adp spec 092,
/// T018): relabelled when its target is renamed while the graph is open, and marked broken, under the name it cached, when the
/// target is gone.
/// </summary>
public sealed class DependencyGraphLinkedLabelsTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("adp-dependency-graph-linked-labels");
    private readonly DependencyGraphDocumentStore _store = new(new FileWriter());
    private readonly DependencyGraphElementMapper _mapper = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ATargetRenamedWhileOpen_RelabelsTheNode()
    {
        // Arrange.
        (DependencyGraphSession session, LinkWorkspace workspace, DependencyGraphOpeningIndex index) = await OpenAsync();
        await using var _ = session;
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        workspace.Put(Target("Check-out"));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.NotNull(received);
        var shown = Payload(received);
        Assert.Equal("Check-out", shown.Label);
        Assert.True(shown.Linked);
    }

    [Fact]
    public async Task ATargetDeletedWhileOpen_MarksTheNodeBroken_UnderItsCachedName()
    {
        // Arrange.
        (DependencyGraphSession session, LinkWorkspace workspace, DependencyGraphOpeningIndex index) = await OpenAsync("Check-out");
        await using var _ = session;
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        workspace.Put(new IndexedDocument(Held("features.yaml"), "k7q2m9xa", "Name", [], []));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.NotNull(received);
        var shown = Payload(received);
        Assert.Equal("Checkout", shown.Label);
        Assert.False(shown.Linked);
    }

    private async Task<(DependencyGraphSession Session, LinkWorkspace Workspace, DependencyGraphOpeningIndex Index)> OpenAsync(string name = "Checkout")
    {
        var path = IoPath.Combine(_folder.Path, "services.dgr");
        await File.WriteAllTextAsync(path, "dependencies: 1\nid: s2v5b9hu\nelements:\n  - id: pay\n    link: $k7q2m9xa@features.yaml#checkout.Name=Checkout\n    x: 0\n    row: 2\n", TestContext.Current.CancellationToken);
        _store.Forget(path);
        var workspace = new LinkWorkspace(_folder.Path.Replace('\\', '/'), [Target(name)]);
        var index = new DependencyGraphOpeningIndex(workspace);
        var session = new DependencyGraphSession(path, _store, _mapper, links: index);
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        return (session, workspace, index);
    }

    private static DependencyGraphElementPayload Payload(IReadOnlyList<DiagramDelta> deltas)
    {
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
        return DependencyGraphElementPayload.Parser.ParseFrom(add.Elements.Single(element => element.Id == "pay").Payload.Span);
    }

    private string Held(string relative) => _folder.Path.Replace('\\', '/') + "/" + relative;

    private IndexedDocument Target(string name) =>
        new(Held("features.yaml"), "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", name)])], []);
}
