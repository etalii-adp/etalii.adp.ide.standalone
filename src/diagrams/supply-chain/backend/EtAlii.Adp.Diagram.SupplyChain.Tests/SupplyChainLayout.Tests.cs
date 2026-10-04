using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The layered layout: goods flow left to right, groups hold their members, and nothing overlaps.</summary>
public sealed class SupplyChainLayoutTests
{
    public static TheoryData<string> Examples => [SupplyChainExamples.Automotive, SupplyChainExamples.GpuMemory];

    private static SupplyChainModel Read(string path) => SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(path)));

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryFlow_RunsLeftToRight(string example)
    {
        // Act.
        var layout = SupplyChainLayout.Of(Read(example));

        // Assert.
        Assert.NotEmpty(layout.Flows);
        Assert.All(layout.Flows, flow => Assert.True(
            layout.NodeBoxes[flow.From].Right < layout.NodeBoxes[flow.To].X,
            $"{flow.Id} runs from {layout.NodeBoxes[flow.From].X} to {layout.NodeBoxes[flow.To].X}"));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EachNode_StandsNoEarlierThanItsStage_AndASourceExactlyAtIt(string example)
    {
        // Act.
        var layout = SupplyChainLayout.Of(Read(example));

        // Assert: a column is a stage. A node nothing supplies stands in its own stage's column - a
        // tier-1 supplier does not stand among the mines - and no node stands left of its stage.
        var supplied = layout.Flows.Select(flow => flow.To).ToHashSet(StringComparer.Ordinal);
        foreach (var node in layout.Nodes)
        {
            var column = layout.NodeBoxes[node.Id].X / SupplyChainLayout.LayerPitch;
            var stage = SupplyChainNodeTypes.RankOf(node.Type);
            Assert.True(column >= stage, $"{node.Id} ({node.Type}) stands in column {column}");
            if (!supplied.Contains(node.Id))
            {
                Assert.Equal(stage, column);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void NoFlow_CrossesACard(string example)
    {
        // Act.
        var layout = SupplyChainLayout.Of(Read(example));

        // Assert: the band, drawn as the canvas draws it - a horizontal S from the supplier's right side
        // through the middle of every lane slot to the consumer's left side - touches no card but its own two.
        foreach (var flow in layout.Flows)
        {
            (SupplyChainBox from, SupplyChainBox to) = (layout.NodeBoxes[flow.From], layout.NodeBoxes[flow.To]);
            var stops = new List<(double X, double Y)> { (from.Right, from.CentreY) };
            foreach ((double laneX, double laneY) in layout.Lanes.GetValueOrDefault(flow.Id) ?? [])
            {
                stops.Add((laneX - (SupplyChainGeometry.NodeWidth / 2), laneY));
                stops.Add((laneX + (SupplyChainGeometry.NodeWidth / 2), laneY));
            }

            stops.Add((to.X, to.CentreY));
            foreach ((string id, SupplyChainBox box) in layout.NodeBoxes.Where(pair => pair.Key != flow.From && pair.Key != flow.To))
            {
                foreach ((double x, double y) in Sampled(stops))
                {
                    Assert.False(x > box.X && x < box.Right && y > box.Y && y < box.Bottom, $"{flow.Id} crosses {id} at ({x:0}, {y:0})");
                }
            }
        }
    }

    /// <summary>Points along horizontal S curves from each stop to the next, as the canvas's flow path bends them.</summary>
    private static IEnumerable<(double X, double Y)> Sampled(List<(double X, double Y)> stops)
    {
        for (var i = 1; i < stops.Count; i++)
        {
            ((double X, double Y) start, (double X, double Y) end) = (stops[i - 1], stops[i]);
            var direction = end.X >= start.X ? 1 : -1;
            var pull = Math.Max(48, Math.Abs(end.X - start.X) / 2);
            (double c1X, double c1Y, double c2X, double c2Y) = (start.X + (direction * pull), start.Y, end.X - (direction * pull), end.Y);
            for (var t = 0.0; t <= 1; t += 0.02)
            {
                var u = 1 - t;
                yield return (
                    (u * u * u * start.X) + (3 * u * u * t * c1X) + (3 * u * t * t * c2X) + (t * t * t * end.X),
                    (u * u * u * start.Y) + (3 * u * u * t * c1Y) + (3 * u * t * t * c2Y) + (t * t * t * end.Y));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void NoTwoArrangedNodes_Overlap(string example)
    {
        // Act.
        var boxes = SupplyChainLayout.Of(Read(example)).NodeBoxes.Values.ToList();

        // Assert.
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                (SupplyChainBox a, SupplyChainBox b) = (boxes[i], boxes[j]);
                Assert.False(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom, $"{a} overlaps {b}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryGroupFrame_HoldsItsMembers(string example)
    {
        // Act.
        var layout = SupplyChainLayout.Of(Read(example));

        // Assert.
        Assert.NotEmpty(layout.Groups);
        foreach (var node in layout.Nodes.Where(node => node.Group.Length > 0))
        {
            (SupplyChainBox frame, SupplyChainBox box) = (layout.GroupBoxes[node.Group], layout.NodeBoxes[node.Id]);
            Assert.True(frame.X < box.X && frame.Y < box.Y && frame.Right > box.Right && frame.Bottom > box.Bottom, $"{node.Id} is outside {node.Group}");
        }
    }

    [Fact]
    public void ACycle_IsLaidOut_RatherThanLoopingForever()
    {
        // Arrange.
        var model = SupplyChainParser.Parse(LineDocument.Parse("""
            supply-chain: 1
            nodes:
              - id: a
                type: supplier
              - id: b
                type: manufacturer
              - id: c
                type: consumer
            flows:
              - id: ab
                from: a
                to: b
              - id: bc
                from: b
                to: c
              - id: ca
                from: c
                to: a
            """));

        // Act.
        var layout = SupplyChainLayout.Of(model);

        // Assert.
        Assert.Equal(3, layout.NodeBoxes.Count);
        Assert.True(layout.NodeBoxes["a"].X < layout.NodeBoxes["b"].X);
        Assert.True(layout.NodeBoxes["b"].X < layout.NodeBoxes["c"].X);
    }

    [Fact]
    public void AnAuthoredPosition_WinsOverTheArrangedOne()
    {
        // Arrange.
        var model = SupplyChainParser.Parse(LineDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.supply"))));

        // Act.
        var layout = SupplyChainLayout.Of(model);

        // Assert.
        Assert.Equal((800d, 40d), (layout.NodeBoxes["shop"].X, layout.NodeBoxes["shop"].Y));
        Assert.NotEqual((800d, 40d), (layout.Arranged["shop"].X, layout.Arranged["shop"].Y));
    }

    [Fact]
    public void AnEmptyDocument_LaysOutNothing()
    {
        // Act.
        var layout = SupplyChainLayout.Of(SupplyChainModel.Empty);

        // Assert.
        Assert.Empty(layout.NodeBoxes);
        Assert.Empty(layout.GroupBoxes);
    }
}
