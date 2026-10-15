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
}
