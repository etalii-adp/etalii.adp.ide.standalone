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
        // Arrange.
        var document = Corpus();

        // Act.
        var first = Layout(document);
        var second = Layout(document);

        // Assert, first, that the corpus laid out to something. Both checks below are satisfied
        // by two empty layouts: `Assert.Equal(first.Count, second.Count)` is the self-comparing
        // count in its two-collection form - 0 == 0 - and `Assert.All` over nothing passes
        // without running its body. Determinism over an empty result is not determinism.
        Assert.True(
            first.Count > 0,
            "The corpus laid out to no boxes, so this guard compared two empty layouts and proved nothing about determinism.");

        // Assert.
        Assert.Equal(first.Count, second.Count);
        Assert.All(first, entry => Assert.Equal(entry.Value, second[entry.Key]));
    }

    [Fact]
    public void Compute_CentresTheRootOnTheOrigin()
    {
        // Arrange and act.
        var boxes = Layout(Corpus());
        var root = boxes[Corpus().Root.Id];

        // Assert.
        Assert.Equal(0, root.CenterX, 3);
        Assert.Equal(0, root.CenterY, 3);
    }

    [Fact]
    public void Compute_PlacesEveryVisibleNode_AndNoOthers()
    {
        // Arrange.
        var document = Corpus();

        // Act.
        var boxes = Layout(document);

        // Assert.
        Assert.Equal(document.Nodes.Count(), boxes.Count);
    }

    [Fact]
    public void Compute_HonoursFreeplanesPositionAttribute()
    {
        // Arrange.
        var document = Corpus();

        // Act.
        var boxes = Layout(document);

        // Assert.
        Assert.True(boxes["ID_411002937"].X > 0, "Backend is POSITION=right");
        Assert.True(boxes["ID_411002941"].Right < 0, "Diagram types is POSITION=left");
    }

    [Fact]
    public void Compute_AlternatesSidesForUnpositionedBranches()
    {
        // Arrange.
        var document = MindmapDocument.Parse(
            "<map version=\"freeplane 1.11.5\"><node TEXT=\"r\" ID=\"r\">" +
            "<node TEXT=\"a\" ID=\"a\"/><node TEXT=\"b\" ID=\"b\"/><node TEXT=\"c\" ID=\"c\"/></node></map>");

        // Act.
        var boxes = Layout(document);

        // Assert.
        Assert.True(boxes["a"].X > 0);
        Assert.True(boxes["b"].Right < 0);
        Assert.True(boxes["c"].X > 0);
    }

    [Fact]
    public void Compute_SiblingsDoNotOverlap()
    {
        // Arrange.
        var document = Corpus();

        var boxes = Layout(document);

        // Assert, first, that the corpus parsed into something. Every claim below is made
        // inside the loop, so an empty document would satisfy this test without comparing a
        // single pair of boxes.
        Assert.True(
            document.Nodes.Any(),
            "The corpus document parsed to no nodes, so this guard compared nothing.");

        // Act and assert, step by step.
        var compared = 0;
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
                    compared++;
                }
            }
        }

        // The floor belongs on the work done, not on any one collection walked. A childless node
        // legitimately yields no column, and a single-child column legitimately yields no pair,
        // so demanding either be non-empty would assert something untrue of a valid document.
        // What must not be empty is the set of sibling pairs actually compared: without this, a
        // corpus that parsed to a flat list of nodes satisfies "siblings do not overlap" without
        // ever placing two siblings side by side.
        Assert.True(
            compared > 0,
            "No sibling pair was compared, so this guard is satisfied by a document with no siblings to overlap.");
    }

    [Fact]
    public void Compute_PutsChildrenBeyondTheirParent_OnTheParentsSide()
    {
        // Arrange.
        var document = Corpus();

        var boxes = Layout(document);

        // Act and assert, step by step.
        var backend = boxes["ID_411002937"];
        Assert.True(
            document.Find("ID_411002937")!.Children.Count > 0,
            "The right-side parent has no children in the corpus, so this half of the guard checked nothing.");
        foreach (var child in document.Find("ID_411002937")!.Children)
        {
            Assert.True(boxes[child.Id].X >= backend.Right + Metrics.HorizontalGap - 0.01, "a right-side child is not to the right of its parent");
        }

        var diagramTypes = boxes["ID_411002941"];
        Assert.True(
            document.Find("ID_411002941")!.Children.Count > 0,
            "The left-side parent has no children in the corpus, so this half of the guard checked nothing.");
        foreach (var child in document.Find("ID_411002941")!.Children)
        {
            Assert.True(boxes[child.Id].Right <= diagramTypes.X - Metrics.HorizontalGap + 0.01, "a left-side child is not to the left of its parent");
        }
    }

    [Fact]
    public void Compute_AFoldedBranch_HidesItsDescendantsAndTakesNoRoomForThem()
    {
        // Arrange.
        var document = Corpus();
        var hierarchy = document.Find("ID_88117425")!;

        // Act.
        var unfolded = Layout(document);
        var folded = Layout(document, node => node.Id == hierarchy.Id);

        // Assert.
        Assert.True(unfolded.ContainsKey("ID_88117426"));
        Assert.False(folded.ContainsKey("ID_88117426"), "a descendant of a folded node was laid out");
        Assert.True(folded.ContainsKey(hierarchy.Id), "the folded node itself must stay visible");
        // With the branch collapsed the Backend column is shorter, so its siblings sit closer.
        Assert.True(folded["ID_88117425"].Bottom - folded["ID_88117420"].Y < unfolded["ID_88117425"].Bottom - unfolded["ID_88117420"].Y);
    }

    [Fact]
    public void Compute_ALongerTextWidensTheNode()
    {
        // Arrange.
        var document = Corpus();
        var node = document.Find("ID_88117422")!;
        var before = Layout(document)[node.Id].Width;

        // Act.
        document.SetText(node, node.Text + " with a considerably longer label");

        // Assert.
        Assert.True(Layout(document)[node.Id].Width > before);
    }

    [Fact]
    public void Compute_AnEditInOneBranch_LeavesTheOtherSideWhereItWas()
    {
        // Arrange.
        // Stability of unaffected subtrees: the left side does not move when the right side grows.
        var document = Corpus();
        var before = Layout(document);

        document.AddChild(document.Find("ID_88117422")!, "new");

        // Act and assert, step by step.
        var after = Layout(document);
        foreach (var id in new[] { "ID_411002941", "ID_411002942", "ID_411002945", "ID_411002949" })
        {
            Assert.Equal(before[id], after[id]);
        }
    }

    [Fact]
    public void Measure_GivesAnEmptyNodeAMinimumWidth()
    {
        // Arrange, act and assert.
        Assert.Equal(Metrics.MinimumWidth, Metrics.Measure("").Width);
    }

    [Fact]
    public void Compute_KeepsAtLeastTheConfiguredShareOfANodesWidthAroundIt()
    {
        // Arrange.
        // Two long siblings under one parent: the fixed VerticalGap alone would leave them
        // closer than a tenth of their width; the ratio must win (appsettings' Mindmap:MinimumGapRatio).
        var document = MindmapDocument.Parse(
            "<map version=\"freeplane 1.11.5\">\n" +
            "<node TEXT=\"r\" ID=\"root\">\n" +
            "<node TEXT=\"a considerably longer sibling label than most\" ID=\"a\" POSITION=\"right\"/>\n" +
            "<node TEXT=\"another considerably longer sibling label\" ID=\"b\" POSITION=\"right\"/>\n" +
            "</node>\n" +
            "</map>\n");
        var metrics = MindmapMetrics.Default with { MinimumGapRatio = 0.1 };

        var boxes = MindmapLayout.Compute(document.Root, metrics, _ => false);

        // Act and assert, step by step.
        var a = boxes["a"];
        var b = boxes["b"];
        var widest = Math.Max(a.Width, b.Width);
        var verticalDistance = Math.Min(Math.Abs(b.Y - a.Bottom), Math.Abs(a.Y - b.Bottom));
        Assert.True(verticalDistance >= widest * 0.1 - 0.01, $"siblings are {verticalDistance} apart; at least {widest * 0.1} required");

        // The horizontal side of the same rule: a child sits at least the root's share away.
        var root = boxes["root"];
        Assert.True(a.X - root.Right >= root.Width * 0.1 - 0.01, "the child hugs its parent closer than the ratio allows");
    }

    [Fact]
    public void Compute_ChildrenOfOneParent_ShareTheSameHorizontalDistanceFromIt()
    {
        // Arrange.
        // Their near edges align on one column: on the right side every child's left edge, on
        // the left side every child's right edge, sits the same distance from the parent.
        var document = Corpus();
        var boxes = Layout(document);

        // Assert, first, that the filter left anything behind. This sweep narrows twice - to
        // nodes with children, then to nodes the layout placed - and either narrowing could
        // empty it while the document itself is perfectly healthy.
        var parents = document.Nodes.Where(node => node.HasChildren && boxes.ContainsKey(node.Id)).ToArray();
        Assert.True(
            parents.Length > 0,
            "No node has both children and a computed box, so this guard checked no parent at all.");

        // Arrange, continued.
        var checkedParents = 0;
        foreach (var parent in parents)
        {
            var childBoxes = parent.Children.Where(child => boxes.ContainsKey(child.Id)).Select(child => boxes[child.Id]).ToArray();
            if (childBoxes.Length < 2)
            {
                continue;
            }

            checkedParents++;

        // Arrange, continued.
            // Rounded before comparing: box.Right sums two independently rounded doubles, so
            // equal edges can differ in the last bits of the mantissa.
            var onRight = childBoxes[0].CenterX > boxes[parent.Id].CenterX;
            var nearEdges = childBoxes.Select(box => Math.Round(onRight ? box.X : box.Right, 6)).Distinct().ToArray();
            // The root splits its children over two sides; group by side there.
            if (parent.IsRoot)
            {
                foreach (var side in childBoxes.GroupBy(box => box.CenterX > boxes[parent.Id].CenterX))
                {
                    Assert.Single(side.Select(box => Math.Round(side.Key ? box.X : box.Right, 6)).Distinct());
                }

                // Act.
                continue;
            }

            // Assert.
            Assert.Single(nearEdges);
        }

        // The parents collection is floored above, but every parent can take the `continue` on
        // its way past: a corpus in which no parent has two placed children satisfies this test
        // without comparing a single pair of edges. The floor therefore belongs on the parents
        // actually examined, which is the loop's effective work rather than the collection it
        // walks.
        Assert.True(
            checkedParents > 0,
            "No parent had two placed children, so this guard compared no sibling edges at all.");
    }

    [Fact]
    public void Compute_NoTwoVisibleNodes_Overlap()
    {
        // Arrange.
        var boxes = Layout(Corpus()).Values.ToArray();

        // Assert, first, that there are boxes to compare. The pair loop below is the "every pair"
        // idiom, and its inner `j = i + 1` correctly does nothing when there is no pair - but
        // that makes the whole test vacuous on an empty layout, which is the state it would most
        // need to report. Two is the floor because overlap is a property of pairs.
        Assert.True(
            boxes.Length >= 2,
            $"The corpus laid out to {boxes.Length} boxes, so no pair was compared and nothing could have overlapped.");

        // Act and assert, step by step.
        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                var apart = a.Right <= b.X || b.Right <= a.X || a.Bottom <= b.Y || b.Bottom <= a.Y;
                Assert.True(apart, $"boxes at ({a.X},{a.Y}) and ({b.X},{b.Y}) overlap");
            }
        }
    }

    [Fact]
    public void Compute_ALargerConfiguredRatio_SpreadsTheSiblingsFurther()
    {
        // Arrange.
        var document = MindmapDocument.Parse(
            "<map version=\"freeplane 1.11.5\">\n" +
            "<node TEXT=\"r\" ID=\"root\">\n" +
            "<node TEXT=\"a considerably longer sibling label than most\" ID=\"a\" POSITION=\"right\"/>\n" +
            "<node TEXT=\"another considerably longer sibling label\" ID=\"b\" POSITION=\"right\"/>\n" +
            "</node>\n" +
            "</map>\n");

        var near = MindmapLayout.Compute(document.Root, MindmapMetrics.Default with { MinimumGapRatio = 0.1 }, _ => false);
        var far = MindmapLayout.Compute(document.Root, MindmapMetrics.Default with { MinimumGapRatio = 0.5 }, _ => false);

        // Act and assert, step by step.
        Assert.True(Distance(far) > Distance(near), "a larger ratio must push the siblings further apart");
        return;

        static double Distance(IReadOnlyDictionary<string, MindmapBox> boxes) => boxes["b"].Y - boxes["a"].Bottom;
    }
}
