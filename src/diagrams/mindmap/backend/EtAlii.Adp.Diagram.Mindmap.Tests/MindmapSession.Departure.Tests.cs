using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The way mindmap's session deliberately differs from the other sessions' reaction to a document
/// change, permitted by backend-centralization R5.3 and pinned here (task 15). The store side of the
/// same departure - that its change event names the kind of structural change - is pinned by
/// <see cref="MindmapDocumentStoreDepartureTests"/> (task 10).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a DELIBERATE DEPARTURE, not an inconsistency left over from the conversion.</b> Every
/// other session reacts through the shared <see cref="DiagramDocumentChangeHandler"/>: render, diff
/// against what the connection was last given, raise the difference (R5.1). Mindmap's session reacts
/// to the KIND of change its store announces instead - an edit to one node pushes that node's new
/// state, a structural change relays out the visible map and removes exactly the nodes the change
/// names, and only a reload relays out everything. R5.3 says it MAY keep doing so, and the design
/// says it shares the path filter, the raise and the failure handling while supplying its own body.
/// </para>
/// <para>
/// A later reader routing the session through the shared handler, or collapsing the change kinds
/// into one whole-map re-render, will make one of these tests fail, and that is the point. Removing
/// the departure is a requirements change, not a tidy-up.
/// </para>
/// <para>
/// <c>MindmapSessionTests.AnEdit_PushesAnAddOfTheNodesNewState</c> covers the ordinary edit - one
/// add of the edited node - but not why: without a baseline, a whole-map re-diff against an empty
/// delivery would fail it only by accident. These tests hold the same document still and vary only
/// the announced kind, so the kind is the only thing that can explain the difference.
/// </para>
/// </remarks>
public sealed class MindmapSessionDepartureTests : IDisposable
{
    private const string ContextService = "ID_88117420";
    // Inside the Hierarchy branch, which is FOLDED in the fixture: never visible, never delivered.
    private const string HiddenInAFoldedBranch = "ID_88117426";

    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;
    private readonly MindmapElementMapper _mapper = new(MindmapMetrics.Default);
    private readonly IHistoryStack _history;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public MindmapSessionDepartureTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.MindmapSessionDepartureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "architecture.mm");
        File.Copy("Fixtures/architecture.mm", _bodyPath);

        // The same wiring the host uses, as in MindmapSessionTests.
        _services = new ServiceCollection()
            .AddSingleton<MindmapViewState>()
            .AddCommands().AddHierarchyCommandHandlers()
            .AddMindmapCommands()
            .BuildServiceProvider();
        _documents = _services.GetRequiredService<IMindmapDocumentStore>();
        _views = _services.GetRequiredService<MindmapViewState>();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private MindmapSession Open() => new(_watchId, _bodyPath, _documents, _views, _mapper, _history);

    private static IReadOnlyList<DiagramElement> AddedElements(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramAddDelta>().SelectMany(add => add.Elements).ToList();

    private static IReadOnlyList<string> RemovedIds(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds).ToList();

    [Fact]
    public async Task PermittedDepartureR53_TheAnnouncedKind_NotADiff_DecidesWhatIsPushed()
    {
        // Arrange: a connection that already holds the whole visible map, as after its baseline.
        var session = Open();
        var visible = AddedElements(session.Baseline()).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        Assert.True(visible.Count > 1);
        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) => pushes.Add(args.Deltas);
        var document = _documents.GetOrLoad(_bodyPath);

        // Act: the SAME unchanged document, announced first as one node's update, then as a reload.
        _documents.Save(_bodyPath, document, new MindmapNodeUpdated(ContextService));
        _documents.Save(_bodyPath, document, new MindmapReloaded());

        // Assert: nothing differs from what the connection holds, so a diff would push nothing
        // either time. The session pushes what each kind names instead: that one node's state for
        // an update, and a relayout of everything visible for a reload.
        Assert.Equal(2, pushes.Count);
        var update = Assert.Single(pushes[0]);
        Assert.Equal(ContextService, Assert.Single(AddedElements([update])).Id);
        Assert.Equal(visible, AddedElements(pushes[1]).Select(element => element.Id).ToHashSet(StringComparer.Ordinal));
        Assert.Empty(RemovedIds(pushes[1]));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task PermittedDepartureR53_AnEdit_PushesOnlyTheTargetedNodesNewState_NotAWholeMapRelayout()
    {
        // Arrange.
        var session = Open();
        _ = session.Baseline();
        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) => pushes.Add(args.Deltas);
        var document = _documents.GetOrLoad(_bodyPath);

        // Act: a command's edit of one node, saved with the kind it announces.
        document.SetText(document.Find(ContextService)!, "Context service, renamed");
        _documents.Save(_bodyPath, document, new MindmapNodeUpdated(ContextService));

        // Assert: one push, one add, one element - that node, carrying its new text - and no removes.
        var deltas = Assert.Single(pushes);
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
        var element = Assert.Single(add.Elements);
        Assert.Equal(ContextService, element.Id);
        Assert.Equal("Context service, renamed", MindmapNodePayload.Parser.ParseFrom(element.Payload.Span).Text);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task PermittedDepartureR53_AStructuralChange_RemovesTheNodesItNames_EvenOnesThisConnectionWasNeverGiven()
    {
        // Arrange: the connection holds the visible map, which leaves out the folded branch.
        var session = Open();
        var delivered = AddedElements(session.Baseline()).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(HiddenInAFoldedBranch, delivered);
        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) => pushes.Add(args.Deltas);
        var document = _documents.GetOrLoad(_bodyPath);

        // Act: a command removes a node inside the folded branch and announces what it removed.
        document.Remove(document.Find(HiddenInAFoldedBranch)!);
        _documents.Save(_bodyPath, document, new MindmapStructureChanged([HiddenInAFoldedBranch]));

        // Assert: the removal is the one the change names. A diff against what this connection was
        // given could not produce it - the node was never delivered - and would push nothing at all,
        // since nothing visible changed.
        var deltas = Assert.Single(pushes);
        Assert.Equal([HiddenInAFoldedBranch], RemovedIds(deltas));
        Assert.Equal(delivered, AddedElements(deltas).Select(element => element.Id).ToHashSet(StringComparer.Ordinal));
        await session.DisposeAsync();
    }
}
