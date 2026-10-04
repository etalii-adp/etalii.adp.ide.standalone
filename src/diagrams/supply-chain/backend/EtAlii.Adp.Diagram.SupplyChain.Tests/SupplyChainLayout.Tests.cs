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
    public void NoTwoArrangedNodes_Overlap(string example)
    {
        // Act.
        var boxes = SupplyChainLayout.Of(Read(example)).NodeBoxes.Values.ToList();

        // Assert.
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var (a, b) = (boxes[i], boxes[j]);
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
            var (frame, box) = (layout.GroupBoxes[node.Group], layout.NodeBoxes[node.Id]);
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
