using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The session against a real document store: what the baseline carries, what an edit and a
/// fold and a viewport change turn into, all in the core delta vocabulary (Requirement 11).
/// </summary>
public class MindmapSessionTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;
    private readonly MindmapElementMapper _mapper = new(MindmapMetrics.Default);
    private readonly IHistoryStack _history;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public MindmapSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "architecture.mm");
        File.Copy("Fixtures/architecture.mm", _bodyPath);

        // The same wiring the host uses, so a drag-move dispatched by the session runs the
        // real command and lands on a real history.
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

    [Fact]
    public void Baseline_AddsEveryVisibleNode_AsMindmapNodeElements()
    {
        // Arrange.
        var session = Open();

        var baseline = session.Baseline();

        // Act and assert, step by step.
        var elements = AddedElements(baseline);
        // The Hierarchy branch is FOLDED in the file, so its descendants are not visible.
        Assert.Contains(elements, element => element.Id == "ID_411002937");
        Assert.DoesNotContain(elements, element => element.Id == "ID_88117426");
        Assert.All(elements, element => Assert.Equal(MindmapElementMapper.NodeType, element.Type));
    }

    [Fact]
    public void Baseline_PacksTheNodesTextIntoThePayload()
    {
        // Arrange.
        var element = AddedElements(Open().Baseline()).Single(e => e.Id == "ID_88117420");

        // Act and assert, step by step.
        var payload = MindmapNodePayload.Parser.ParseFrom(element.Payload.Span);
        Assert.Equal("Context service", payload.Text);
        Assert.NotNull(payload.Link);
        Assert.Equal("../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs", payload.Link.Raw);
    }

    [Fact]
    public void Baseline_PacksTheMeasuredBoxIntoThePayload()
    {
        // Arrange.
        // The canvas draws each node at its true size and anchors connectors on its actual
        // edges; a payload without the measured box left it guessing a fixed one, and the
        // guesses overlapped on screen (found by the bezier-connector pass).
        var element = AddedElements(Open().Baseline()).Single(e => e.Id == "ID_88117420");

        // Act and assert, step by step.
        var payload = MindmapNodePayload.Parser.ParseFrom(element.Payload.Span);
        var expected = MindmapMetrics.Default.Measure("Context service");
        Assert.Equal(expected.Width, payload.Width, 3);
        Assert.Equal(expected.Height, payload.Height, 3);
    }

    [Fact]
    public async Task AnEdit_PushesAnAddOfTheNodesNewState()
    {
        // Arrange.
        var session = Open();
        DiagramDeltasEventArgs? pushed = null;
        session.Changed += (_, args) => pushed = args;

        // Act.
        var document = _documents.GetOrLoad(_bodyPath); // ensure loaded, as the service would have
        _documents.Save(_bodyPath, document, new MindmapNodeUpdated("ID_88117420"));

        // Assert.
        Assert.NotNull(pushed);
        var element = AddedElements(pushed!.Deltas).Single();
        Assert.Equal("ID_88117420", element.Id);
        await session.DisposeAsync();
    }

    [Fact]
    public void CollapseThroughTheViewState_PushesAGroupWithRepositioning_ThenAnUngroup()
    {
        // Arrange.
        // The whole route the action takes: the provider toggles through MindmapViewState,
        // whose event is what makes this session push - the bug the manual pass found was a
        // toggle that changed state no stream ever heard of.
        var session = Open();
        _ = session.Baseline();
        var pushes = new List<IReadOnlyList<DiagramDelta>>();
        session.Changed += (_, args) => pushes.Add(args.Deltas);

        // Backend is expanded in the file; collapsing it groups its subtree.
        _views.Toggle(_watchId, _bodyPath, _documents.GetOrLoad(_bodyPath), "ID_411002937");

        // Act and assert, step by step.
        var collapse = Assert.Single(pushes);
        var group = Assert.IsType<DiagramGroupDelta>(collapse[0]);
        Assert.Equal("ID_411002937", group.GroupElement.Id);
        Assert.Contains("ID_88117420", group.SourceElementIds);
        // The freed room moves the survivors: the group travels with a repositioning upsert.
        var repositioned = Assert.IsType<DiagramAddDelta>(collapse[1]);
        Assert.DoesNotContain(repositioned.Elements, element => element.Id == "ID_88117420");

        _views.Toggle(_watchId, _bodyPath, _documents.GetOrLoad(_bodyPath), "ID_411002937");

        var expand = pushes[1];
        var ungroup = Assert.IsType<DiagramUngroupDelta>(expand[0]);
        Assert.Equal("ID_411002937", ungroup.GroupElementId);
        Assert.Contains(ungroup.Elements, element => element.Id == "ID_88117420");
    }

    [Fact]
    public void CollapseThroughTheViewState_IsViewStateOnly_AndDoesNotWriteTheFile()
    {
        // Arrange.
        var before = File.ReadAllText(_bodyPath);
        var session = Open();
        _ = session.Baseline();

        // Act.
        _views.Toggle(_watchId, _bodyPath, _documents.GetOrLoad(_bodyPath), "ID_411002937");

        // Assert.
        Assert.Equal(before, File.ReadAllText(_bodyPath));
        Assert.True(_views.For(_watchId, _bodyPath, _documents.GetOrLoad(_bodyPath)).IsFolded("ID_411002937"));
    }

    [Fact]
    public void AnotherConnectionsCollapse_IsNotPushedOnThisStream()
    {
        // Arrange.
        // Folds are per connection (Requirement 9.4): a second viewer collapsing a branch
        // must not regroup it for this one.
        var session = Open();
        _ = session.Baseline();
        var pushed = false;
        session.Changed += (_, _) => pushed = true;

        // Act.
        _views.Toggle(ShortGuid.NewShortGuid(), _bodyPath, _documents.GetOrLoad(_bodyPath), "ID_411002937");

        // Assert.
        Assert.False(pushed);
    }

    [Fact]
    public async Task MoveElement_RunsAsACommand_AndCanBeUndone()
    {
        // Arrange.
        // A drag on the canvas: Hierarchy (third child of Backend) moves under Client.
        var session = Open();
        _ = session.Baseline();

        // Arrange, continued.
        var error = await session.MoveElementAsync("ID_88117425", "ID_411002938", -1, TestContext.Current.CancellationToken);

        // Arrange, continued.
        Assert.Equal("", error);
        var document = _documents.GetOrLoad(_bodyPath);
        Assert.Equal("ID_411002938", document.Find("ID_88117425")!.Parent!.Id);
        Assert.True(_history.CanUndo);

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal("ID_411002937", _documents.GetOrLoad(_bodyPath).Find("ID_88117425")!.Parent!.Id);
    }

    [Fact]
    public async Task MoveElement_IntoItsOwnBranch_IsRefusedWithTheHandlersReason()
    {
        // Arrange.
        var session = Open();
        _ = session.Baseline();

        // Act.
        var error = await session.MoveElementAsync("ID_411002937", "ID_88117422", -1, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("own branch", error, StringComparison.Ordinal);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void UpdateView_DeliversWhatNewlyEntersView_AndRemovesWhatLeaves()
    {
        // Arrange.
        var session = Open();
        _ = session.Baseline(); // the service always streams the baseline first, which loads the document
        var document = _documents.GetOrLoad(_bodyPath);
        var grandchild = document.Root.Children.SelectMany(child => child.Children).First();

        // Arrange, continued.
        // Narrowing to a tiny box around the root. The connection starts unbounded, so this
        // direction is all removals - asserting over the adds here passed against an empty
        // collection and proved nothing.
        var narrow = session.UpdateView(new DiagramViewport(-20, -20, 20, 20));

        // Arrange, continued.
        var removed = narrow.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(document.Root.Id, removed);
        // The root's children survive the narrowing even though the box does not reach them:
        // the canvas anchors each connector on the boxes at both of its ends, so culling them
        // would drop the lines leaving the root (Requirement 11.5).
        Assert.All(document.Root.Children, child => Assert.DoesNotContain(child.Id, removed));
        // One hop and no further, or a viewport would drag in the whole map.
        Assert.Contains(grandchild.Id, removed);

        // Act.
        var wide = session.UpdateView(DiagramViewport.Unbounded);

        // Assert.
        // Widening brings the rest back in; nothing is removed by widening.
        Assert.Contains(AddedElements(wide), element => element.Id == grandchild.Id);
        Assert.Empty(wide.OfType<DiagramRemoveDelta>().SelectMany(remove => remove.ElementIds));
    }

    [Fact]
    public async Task Dispose_ForgetsThisConnectionsView()
    {
        // Arrange.
        var session = Open();
        _ = session.Baseline();
        _views.Toggle(_watchId, _bodyPath, _documents.GetOrLoad(_bodyPath), "ID_411002937");

        // Act.
        await session.DisposeAsync();

        // Assert.
        Assert.Null(_views.Find(_watchId, _bodyPath));
    }
}
