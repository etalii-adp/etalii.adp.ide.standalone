using Xunit;

namespace EtAlii.Adp.Layout.Tests;

public class LayeringTests
{
    [Fact]
    public void LongestPath_PutsANodeAfterItsLongestChain()
    {
        // Arrange: c waits for a directly and for a through b, so the longer chain decides.
        string[] nodes = ["a", "b", "c"];
        (string, string)[] edges = [("a", "b"), ("b", "c"), ("a", "c")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIsZero);

        // Assert.
        Assert.Equal(0, layers["a"]);
        Assert.Equal(1, layers["b"]);
        Assert.Equal(2, layers["c"]);
    }

    [Fact]
    public void LongestPath_IgnoresAnEdgeToAnUnknownNode()
    {
        // Arrange.
        string[] nodes = ["a", "b"];
        (string, string)[] edges = [("missing", "a"), ("a", "missing"), ("a", "b")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIsZero);

        // Assert.
        Assert.Equal(2, layers.Count);
        Assert.Equal(0, layers["a"]);
        Assert.Equal(1, layers["b"]);
    }

    [Fact]
    public void BackEdgeIsZero_CountsTheNodeOnTheStackAsLayerZero()
    {
        // Arrange: a and b wait for each other. The walk starts at a, reaches b, and meets a on its
        // stack: a counts as zero there, so b is one, and a, waiting for b, is two.
        string[] nodes = ["a", "b"];
        (string, string)[] edges = [("b", "a"), ("a", "b")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIsZero);

        // Assert.
        Assert.Equal(2, layers["a"]);
        Assert.Equal(1, layers["b"]);
    }

    [Fact]
    public void BackEdgeIsZero_ASelfLoopMovesItsNodeOneLayer()
    {
        // Arrange.
        string[] nodes = ["a"];
        (string, string)[] edges = [("a", "a")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIsZero);

        // Assert.
        Assert.Equal(1, layers["a"]);
    }
}
