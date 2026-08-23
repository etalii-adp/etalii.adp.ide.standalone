using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;
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
    private readonly MindmapDocumentStore _documents = new();
    private readonly MindmapViewState _views = new();
    private readonly MindmapElementMapper _mapper = new(MindmapMetrics.Default);
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public MindmapSessionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "architecture.mm");
        File.Copy("Fixtures/architecture.mm", _bodyPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MindmapSession Open() => new(_watchId, _bodyPath, _documents, _views, _mapper);

    private static IReadOnlyList<DiagramElement> AddedElements(IReadOnlyList<DiagramDelta> deltas) =>
        deltas.OfType<DiagramDelta.Add>().SelectMany(add => add.Elements).ToList();

    [Fact]
    public void Baseline_AddsEveryVisibleNode_AsMindmapNodeElements()
    {
        var session = Open();

        var baseline = session.Baseline();

        var elements = AddedElements(baseline);
        // The Hierarchy branch is FOLDED in the file, so its descendants are not visible.
        Assert.Contains(elements, element => element.Id == "ID_411002937");
        Assert.DoesNotContain(elements, element => element.Id == "ID_88117426");
        Assert.All(elements, element => Assert.Equal(MindmapElementMapper.NodeType, element.Type));
    }

    [Fact]
    public void Baseline_PacksTheNodesTextIntoThePayload()
    {
        var element = AddedElements(Open().Baseline()).Single(e => e.Id == "ID_88117420");

        var payload = MindmapNodePayload.Parser.ParseFrom(element.Payload.Span);
        Assert.Equal("Context service", payload.Text);
        Assert.NotNull(payload.Link);
        Assert.Equal("../../../../backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs", payload.Link.Raw);
    }

    [Fact]
    public async Task AnEdit_PushesAnAddOfTheNodesNewState()
    {
        var session = Open();
        DiagramDeltasEventArgs? pushed = null;
        session.Changed += (_, args) => pushed = args;

        _documents.GetOrLoad(_bodyPath); // ensure loaded, as the service would have
        _documents.Save(_bodyPath, new MindmapChange.NodeUpdated("ID_88117420"));

        Assert.NotNull(pushed);
        var element = AddedElements(pushed!.Deltas).Single();
        Assert.Equal("ID_88117420", element.Id);
        await session.DisposeAsync();
    }

    [Fact]
    public void ToggleFold_PushesAGroupOfTheHiddenBranch_ThenAnUngroup()
    {
        var session = Open();
        _ = session.Baseline();

        // Backend is unfolded in the file; folding it groups its subtree.
        var folded = session.ToggleFold("ID_411002937");
        var group = Assert.IsType<DiagramDelta.Group>(Assert.Single(folded));
        Assert.Equal("ID_411002937", group.GroupElement.Id);
        Assert.Contains("ID_88117420", group.SourceElementIds);

        var unfolded = session.ToggleFold("ID_411002937");
        var ungroup = Assert.IsType<DiagramDelta.Ungroup>(Assert.Single(unfolded));
        Assert.Equal("ID_411002937", ungroup.GroupElementId);
        Assert.Contains(ungroup.Elements, element => element.Id == "ID_88117420");
    }

    [Fact]
    public void ToggleFold_IsViewStateOnly_AndDoesNotWriteTheFile()
    {
        var before = File.ReadAllText(_bodyPath);
        var session = Open();
        _ = session.Baseline();

        session.ToggleFold("ID_411002937");

        Assert.Equal(before, File.ReadAllText(_bodyPath));
        Assert.True(_views.For(_watchId, _bodyPath, _documents.GetOrLoad(_bodyPath)).IsFolded("ID_411002937"));
    }

    [Fact]
    public void UpdateView_DeliversWhatNewlyEntersView_AndRemovesWhatLeaves()
    {
        var session = Open();
        _ = session.Baseline(); // the service always streams the baseline first, which loads the document
        // A tiny box around the root only.
        var narrow = session.UpdateView(new DiagramViewport(-20, -20, 20, 20));
        Assert.All(AddedElements(narrow), element => Assert.Equal(_documents.GetOrLoad(_bodyPath).Root.Id, element.Id));

        var wide = session.UpdateView(DiagramViewport.Unbounded);

        // Widening brings the branches in; nothing is removed by widening.
        Assert.Contains(AddedElements(wide), element => element.Id == "ID_411002937");
        Assert.Empty(wide.OfType<DiagramDelta.Remove>().SelectMany(remove => remove.ElementIds));
    }

    [Fact]
    public async Task Dispose_ForgetsThisConnectionsView()
    {
        var session = Open();
        _ = session.Baseline();
        session.ToggleFold("ID_411002937");

        await session.DisposeAsync();

        Assert.Null(_views.Find(_watchId, _bodyPath));
    }
}
