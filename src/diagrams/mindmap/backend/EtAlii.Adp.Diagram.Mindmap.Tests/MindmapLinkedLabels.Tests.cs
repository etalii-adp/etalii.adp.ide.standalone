using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Links;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// A linked node of an open map shows its target as the workspace now holds it (DISL 4.12, FBL 17.1, etalii.adp spec 092,
/// T018): named by the target's current name, measured by it, renamed while the map is open, and marked broken, under the
/// text it cached, when the target is gone.
/// </summary>
public sealed class MindmapLinkedLabelsTests : IDisposable
{
    private const string Map = "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"Shop relaunch\" ID=\"ID_1\">\n<node TEXT=\"Checkout\" ID=\"ID_2\">\n<attribute NAME=\"adp-link\" VALUE=\"$k7q2m9xa@features.yaml#checkout.Name=Checkout\"/>\n</node>\n<node TEXT=\"Ask the support team\" ID=\"ID_5\"/>\n</node>\n</map>\n";

    private readonly TestFolder _folder = TestFolder.Create("mindmap-linked-labels");
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddSingleton<MindmapViewState>()
        .AddCommands().AddHierarchyCommandHandlers()
        .AddMindmapCommands()
        .BuildServiceProvider();
    private readonly MindmapElementMapper _mapper = new(MindmapMetrics.Default);

    public void Dispose()
    {
        _services.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ALinkedNode_IsNamedAndMeasuredByItsTargetsCurrentName()
    {
        // Arrange: the target was renamed, to a longer name, since the node cached its text.
        await using var session = Open("Checkout and payment").Session;

        // Act.
        var baseline = await session.BaselineAsync(TestContext.Current.CancellationToken);

        // Assert.
        var node = Node(baseline);
        var shown = MindmapNodePayload.Parser.ParseFrom(node.Payload.Span);
        Assert.Equal("Checkout and payment", shown.Text);
        Assert.Equal("$k7q2m9xa@features.yaml#checkout.Name=Checkout", shown.AdpLink);
        Assert.True(shown.Linked);
        Assert.Equal(MindmapMetrics.Default.Measure("Checkout and payment").Width, shown.Width);
    }

    [Fact]
    public async Task ATargetRenamedWhileOpen_RenamesTheNode()
    {
        // Arrange.
        (MindmapSession session, LinkWorkspace workspace, MindmapOpeningIndex index) = Open("Checkout");
        await using var _ = session;
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        workspace.Put(Target("Pay"));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.NotNull(received);
        var shown = MindmapNodePayload.Parser.ParseFrom(Node(received).Payload.Span);
        Assert.Equal("Pay", shown.Text);
        Assert.True(shown.Linked);
    }

    [Fact]
    public async Task ATargetDeletedWhileOpen_MarksTheNodeBroken_UnderItsCachedText()
    {
        // Arrange.
        (MindmapSession session, LinkWorkspace workspace, MindmapOpeningIndex index) = Open("Pay");
        await using var _ = session;
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        workspace.Put(new IndexedDocument(Held("features.yaml"), "k7q2m9xa", "Name", [], []));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.NotNull(received);
        var shown = MindmapNodePayload.Parser.ParseFrom(Node(received).Payload.Span);
        Assert.Equal("Checkout", shown.Text);
        Assert.False(shown.Linked);
    }

    [Fact]
    public async Task AnOrdinaryNode_CarriesNoLink()
    {
        // Arrange.
        await using var session = Open("Checkout").Session;

        // Act.
        var baseline = await session.BaselineAsync(TestContext.Current.CancellationToken);

        // Assert.
        var shown = MindmapNodePayload.Parser.ParseFrom(baseline.OfType<DiagramAddDelta>().SelectMany(add => add.Elements).Single(element => element.Id == "ID_5").Payload.Span);
        Assert.Equal("", shown.AdpLink);
        Assert.False(shown.Linked);
    }

    private (MindmapSession Session, LinkWorkspace Workspace, MindmapOpeningIndex Index) Open(string name)
    {
        var path = IoPath.Combine(_folder.Path, "relaunch.mm");
        File.WriteAllText(path, Map);
        var workspace = new LinkWorkspace(_folder.Path.Replace('\\', '/'), [Target(name)]);
        var index = new MindmapOpeningIndex(workspace);
        var history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_folder.Path));
        var session = new MindmapSession(ShortGuid.NewShortGuid(), path, _services.GetRequiredService<IMindmapDocumentStore>(), _services.GetRequiredService<MindmapViewState>(), _mapper, history, index);
        return (session, workspace, index);
    }

    private static DiagramElement Node(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(add => add.Elements).Last(element => element.Id == "ID_2");

    private string Held(string relative) => _folder.Path.Replace('\\', '/') + "/" + relative;

    private IndexedDocument Target(string name) =>
        new(Held("features.yaml"), "k7q2m9xa", "Name", [new IndexedElement("checkout", [new KeyValuePair<string, string>("Name", name)])], []);
}
