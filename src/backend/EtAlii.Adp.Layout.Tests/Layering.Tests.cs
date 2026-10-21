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
    [InlineData(CyclePolicy.Kahn)]
    [InlineData(CyclePolicy.Relaxation)]
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

    [Fact]
    public void Kahn_LeavesACycleUnreleased_WithWhatItsReleasedPredecessorsGave()
    {
        // Arrange: c comes before a, and a and b wait for each other. c is released and gives a
        // layer one; a still waits for b, so neither is ever released and b keeps zero.
        string[] nodes = ["c", "a", "b"];
        (string, string)[] edges = [("c", "a"), ("a", "b"), ("b", "a")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, CyclePolicy.Kahn);

        // Assert.
        Assert.Equal(0, layers["c"]);
        Assert.Equal(1, layers["a"]);
        Assert.Equal(0, layers["b"]);
    }

    [Theory]
    [InlineData(CyclePolicy.BackEdgeIsZero)]
    [InlineData(CyclePolicy.BackEdgeIgnored)]
    [InlineData(CyclePolicy.BackEdgeIsZeroPerNode)]
    [InlineData(CyclePolicy.Kahn)]
    [InlineData(CyclePolicy.Relaxation)]
    public void AFloor_RaisesItsNode_AndPushesWhatComesAfter(CyclePolicy policy)
    {
        // Arrange: b may sit no further left than layer three, so c, after it, is four.
        string[] nodes = ["a", "b", "c"];
        (string, string)[] edges = [("a", "b"), ("b", "c")];

        // Act.
        var layers = Layering.LongestPath(nodes, edges, policy, node => node == "b" ? 3 : 0);

        // Assert.
        Assert.Equal(0, layers["a"]);
        Assert.Equal(3, layers["b"]);
        Assert.Equal(4, layers["c"]);
    }

    [Fact]
    public void Relaxation_LetsACycleClimbALayerARound_UntilTheBound()
    {
        // Arrange: a and b wait for each other. Two nodes, so two rounds: a lifts b to one and b
        // lifts a to two, then b to three and a to four, and the bound stops it there.
        (string, string)[] edges = [("a", "b"), ("b", "a")];

        // Act.
        var aFirst = Layering.LongestPath(["a", "b"], edges, CyclePolicy.Relaxation);
        var bFirst = Layering.LongestPath(["b", "a"], edges, CyclePolicy.Relaxation);

        // Assert: where the members end depends on the order they are walked in.
        Assert.Equal(4, aFirst["a"]);
        Assert.Equal(3, aFirst["b"]);
        Assert.Equal(3, bFirst["a"]);
        Assert.Equal(4, bFirst["b"]);
    }

    [Fact]
    public void AcyclicEdges_LeaveOutTheEdgeThatClosesACycle()
    {
        // Arrange: a -> b -> c -> a, walked from a, so c -> a is the edge that closes it.
        string[] nodes = ["a", "b", "c"];
        var outgoing = new Dictionary<string, List<string>> { ["a"] = ["b"], ["b"] = ["c"], ["c"] = ["a"] };

        // Act.
        var forward = Layering.AcyclicEdges(nodes, outgoing);

        // Assert.
        Assert.Equal(["b"], forward["a"]);
        Assert.Equal(["c"], forward["b"]);
        Assert.Empty(forward["c"]);
    }

    [Fact]
    public void AcyclicEdges_KeepEveryEdgeOfADiamond()
    {
        // Arrange: two paths from a to d; the second reaches d after it is finished.
        string[] nodes = ["a", "b", "c", "d"];
        var outgoing = new Dictionary<string, List<string>> { ["a"] = ["b", "c"], ["b"] = ["d"], ["c"] = ["d"], ["d"] = [] };

        // Act.
        var forward = Layering.AcyclicEdges(nodes, outgoing);

        // Assert.
        Assert.Equal(["b", "c"], forward["a"]);
        Assert.Equal(["d"], forward["b"]);
        Assert.Equal(["d"], forward["c"]);
        Assert.Empty(forward["d"]);
    }
}
