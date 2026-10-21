using Xunit;

namespace EtAlii.Adp.Layout.Tests;

public class OverlapResolverTests
{
    [Fact]
    public void Separate_LeavesBoxesThatAreClear_WhereTheyAre()
    {
        // Arrange: two 10 by 10 boxes 20 apart, keeping 5 between them.
        (double X, double Y)[] centres = [(0, 0), (20, 0)];
        (double, double)[] sizes = [(10, 10), (10, 10)];

        // Act.
        var remaining = OverlapResolver.Separate(centres, sizes, 5, 10, 0.5);

        // Assert.
        Assert.Equal(0, remaining);
        Assert.Equal([(0d, 0d), (20d, 0d)], centres);
    }

    [Fact]
    public void Separate_PushesAPairApart_AlongTheAxisThatNeedsLeast()
    {
        // Arrange: side by side and 4 apart, so across needs 11 more and down needs 15.
        (double X, double Y)[] centres = [(0, 0), (4, 0)];
        (double, double)[] sizes = [(10, 10), (10, 10)];

        // Act.
        var remaining = OverlapResolver.Separate(centres, sizes, 5, 10, 0.5);

        // Assert: each moved half the overlap and the nudge, across only.
        Assert.Equal(0, remaining);
        Assert.Equal([(-6d, 0d), (10d, 0d)], centres);
    }

    [Fact]
    public void Separate_ReportsThePairsStillOverlapping_WhenTheRoundsRunOut()
    {
        // Arrange: three boxes on one spot, and a single round.
        (double X, double Y)[] centres = [(0, 0), (0, 0), (0, 0)];
        (double, double)[] sizes = [(10, 10), (10, 10), (10, 10)];

        // Act.
        var remaining = OverlapResolver.Separate(centres, sizes, 5, 1, 0.5);

        // Assert.
        Assert.True(remaining > 0);
    }

    [Fact]
    public void PushOffAlongAxes_MovesTheMoverTheShorterWayAlongItsAxis_AndLeavesItsBlocker()
    {
        // Arrange: the mover overlaps its blocker by 2 on the right, so left is 4 away and right 20.
        Rect[] boxes = [new(0, 0, 10, 10), new(8, 0, 10, 10)];

        // Act.
        var cleared = OverlapResolver.PushOffAlongAxes(boxes, [(0, true)], [0, 1], separation: 2, decimals: 2);

        // Assert.
        Assert.True(cleared);
        Assert.Equal(new Rect(-4, 0, 10, 10), boxes[0]);
        Assert.Equal(new Rect(8, 0, 10, 10), boxes[1]);
    }

    [Fact]
    public void PushOffAlongAxes_MovesAMoverAlongY_WhenThatIsItsAxis()
    {
        // Arrange: the blocker sits just above, so down is 4 away and up 20.
        Rect[] boxes = [new(0, 0, 10, 10), new(0, -8, 10, 10)];

        // Act.
        OverlapResolver.PushOffAlongAxes(boxes, [(0, false)], [0, 1], separation: 2, decimals: 2);

        // Assert.
        Assert.Equal(new Rect(0, 4, 10, 10), boxes[0]);
    }

    [Fact]
    public void PushOffAlongAxes_BreaksATieByRank_SoTwoCoincidentBoxesDoNotMoveInLockstep()
    {
        // Arrange: two boxes in one place, both moving along X; left and right are equally far.
        Rect[] lowerFirst = [new(0, 0, 10, 10), new(0, 0, 10, 10)];
        Rect[] higherFirst = [new(0, 0, 10, 10), new(0, 0, 10, 10)];

        // Act.
        OverlapResolver.PushOffAlongAxes(lowerFirst, [(0, true), (1, true)], [0, 1], separation: 2, decimals: 2);
        OverlapResolver.PushOffAlongAxes(higherFirst, [(0, true), (1, true)], [1, 0], separation: 2, decimals: 2);

        // Assert: the lower rank goes left, the higher right, and the other is then clear of it.
        Assert.Equal(-12, lowerFirst[0].X);
        Assert.Equal(0, lowerFirst[1].X);
        Assert.Equal(12, higherFirst[0].X);
        Assert.Equal(0, higherFirst[1].X);
    }

    [Fact]
    public void PushOffAlongAxes_RoundsANewCoordinate_ToTheGivenPlaces()
    {
        // Arrange.
        Rect[] boxes = [new(0, 0, 10, 10), new(8.333, 0, 10, 10)];

        // Act.
        OverlapResolver.PushOffAlongAxes(boxes, [(0, true)], [0, 1], separation: 0, decimals: 2);

        // Assert.
        Assert.Equal(-1.67, boxes[0].X);
    }
}
