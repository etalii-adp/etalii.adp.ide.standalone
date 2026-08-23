using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>Positions only, no canvas: the layout is a function and is tested as one (Requirement 5.5).</summary>
public class MindmapLayoutTests
{
    private static readonly MindmapMetrics Metrics = MindmapMetrics.Default;

    private static MindmapDocument Corpus() => MindmapDocument.Parse(File.ReadAllText("Fixtures/architecture.mm"));

    private static bool Nothing(MindmapNode _) => false;

    private static IReadOnlyDictionary<string, MindmapBox> Layout(MindmapDocument document, Func<MindmapNode, bool>? isFolded = null) =>
        MindmapLayout.Compute(document.Root, Metrics, isFolded ?? Nothing);

    [Fact]
    public void Compute_IsDeterministic()
    {
        var document = Corpus();

        var first = Layout(document);
        var second = Layout(document);

        Assert.Equal(first.Count, second.Count);
        Assert.All(first, entry => Assert.Equal(entry.Value, second[entry.Key]));
    }

    [Fact]
    public void Compute_CentresTheRootOnTheOrigin()
    {
        var boxes = Layout(Corpus());
        var root = boxes[Corpus().Root.Id];

        Assert.Equal(0, root.CenterX, 3);
        Assert.Equal(0, root.CenterY, 3);
    }

    [Fact]
    public void Compute_PlacesEveryVisibleNode_AndNoOthers()
    {
        var document = Corpus();

        var boxes = Layout(document);

        Assert.Equal(document.Nodes.Count(), boxes.Count);
    }

    [Fact]
    public void Compute_HonoursFreeplanesPositionAttribute()
    {
        var document = Corpus();

        var boxes = Layout(document);

        Assert.True(boxes["ID_411002937"].X > 0, "Backend is POSITION=right");
        Assert.True(boxes["ID_411002941"].Right < 0, "Diagram types is POSITION=left");
    }

    [Fact]
    public void Compute_AlternatesSidesForUnpositionedBranches()
    {
        var document = MindmapDocument.Parse(
            "<map version=\"freeplane 1.11.5\"><node TEXT=\"r\" ID=\"r\">" +
            "<node TEXT=\"a\" ID=\"a\"/><node TEXT=\"b\" ID=\"b\"/><node TEXT=\"c\" ID=\"c\"/></node></map>");

        var boxes = Layout(document);

        Assert.True(boxes["a"].X > 0);
        Assert.True(boxes["b"].Right < 0);
        Assert.True(boxes["c"].X > 0);
    }

    [Fact]
    public void Compute_SiblingsDoNotOverlap()
    {
        var document = Corpus();

        var boxes = Layout(document);

        foreach (var node in document.Nodes)
        {
            // The root's children split across two sides; only siblings on one side share a column.
            foreach (var column in node.Children.GroupBy(child => boxes[child.Id].X >= 0))
            {
                var children = column.ToArray();
                for (var i = 1; i < children.Length; i++)
                {
                    var above = boxes[children[i - 1].Id];
                    var below = boxes[children[i].Id];
                    Assert.True(below.Y >= above.Bottom, $"'{children[i].Text}' overlaps '{children[i - 1].Text}'");
                }
            }
        }
    }

    [Fact]
    public void Compute_PutsChildrenBeyondTheirParent_OnTheParentsSide()
    {
        var document = Corpus();

        var boxes = Layout(document);

        var backend = boxes["ID_411002937"];
        foreach (var child in document.Find("ID_411002937")!.Children)
        {
            Assert.True(boxes[child.Id].X >= backend.Right + Metrics.HorizontalGap - 0.01, "a right-side child is not to the right of its parent");
        }

        var diagramTypes = boxes["ID_411002941"];
        foreach (var child in document.Find("ID_411002941")!.Children)
        {
            Assert.True(boxes[child.Id].Right <= diagramTypes.X - Metrics.HorizontalGap + 0.01, "a left-side child is not to the left of its parent");
        }
    }

    [Fact]
    public void Compute_AFoldedBranch_HidesItsDescendantsAndTakesNoRoomForThem()
    {
        var document = Corpus();
        var hierarchy = document.Find("ID_88117425")!;

        var unfolded = Layout(document);
        var folded = Layout(document, node => node.Id == hierarchy.Id);

        Assert.True(unfolded.ContainsKey("ID_88117426"));
        Assert.False(folded.ContainsKey("ID_88117426"), "a descendant of a folded node was laid out");
        Assert.True(folded.ContainsKey(hierarchy.Id), "the folded node itself must stay visible");
        // With the branch collapsed the Backend column is shorter, so its siblings sit closer.
        Assert.True(folded["ID_88117425"].Bottom - folded["ID_88117420"].Y < unfolded["ID_88117425"].Bottom - unfolded["ID_88117420"].Y);
    }

    [Fact]
    public void Compute_ALongerTextWidensTheNode()
    {
        var document = Corpus();
        var node = document.Find("ID_88117422")!;
        var before = Layout(document)[node.Id].Width;

        document.SetText(node, node.Text + " with a considerably longer label");

        Assert.True(Layout(document)[node.Id].Width > before);
    }

    [Fact]
    public void Compute_AnEditInOneBranch_LeavesTheOtherSideWhereItWas()
    {
        // Stability of unaffected subtrees: the left side does not move when the right side grows.
        var document = Corpus();
        var before = Layout(document);

        document.AddChild(document.Find("ID_88117422")!, "new");

        var after = Layout(document);
        foreach (var id in new[] { "ID_411002941", "ID_411002942", "ID_411002945", "ID_411002949" })
        {
            Assert.Equal(before[id], after[id]);
        }
    }

    [Fact]
    public void Measure_GivesAnEmptyNodeAMinimumWidth()
    {
        Assert.Equal(Metrics.MinimumWidth, Metrics.Measure("").Width);
    }
}
