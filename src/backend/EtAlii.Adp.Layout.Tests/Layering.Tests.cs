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

    [Fact]
    public void BackEdgeIgnored_LetsTheEdgeIntoTheStackPushNothing()
    {
        // Arrange: a and b wait for each other. The walk starts at a, reaches b, and meets a on its
        // stack: that edge is left out, so b is zero, and a, waiting for b, is one.
        string[] nodes = ["a", "b"];
        (string, string)[] edges = [("b", "a"), ("a", "b")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIgnored);

        // Assert.
        Assert.Equal(1, layers["a"]);
        Assert.Equal(0, layers["b"]);
    }

    [Fact]
    public void BackEdgeIsZeroPerNode_MeasuresEveryCycleMemberFromItself()
    {
        // Arrange: the same two-node cycle. Each walk starts at its own node, so neither member's
        // layer depends on which was walked first.
        string[] nodes = ["a", "b"];
        (string, string)[] edges = [("b", "a"), ("a", "b")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIsZeroPerNode);

        // Assert.
        Assert.Equal(2, layers["a"]);
        Assert.Equal(2, layers["b"]);
    }

    [Theory]
    [InlineData(CyclePolicy.BackEdgeIgnored)]
    [InlineData(CyclePolicy.BackEdgeIsZeroPerNode)]
    public void EveryPolicy_AgreesWithBackEdgeIsZero_WithoutACycle(CyclePolicy policy)
    {
        // Arrange: a diamond with a tail, so a node is reached along two paths.
        string[] nodes = ["d", "c", "b", "a", "e"];
        (string, string)[] edges = [("a", "b"), ("a", "c"), ("b", "d"), ("c", "d"), ("d", "e"), ("a", "e")];

        // Act.
        var expected = Layering.LongestPath(nodes, edges, CyclePolicy.BackEdgeIsZero);
        var actual = Layering.LongestPath(nodes, edges, policy);

        // Assert.
        Assert.Equal(expected.OrderBy(pair => pair.Key), actual.OrderBy(pair => pair.Key));
    }
}
