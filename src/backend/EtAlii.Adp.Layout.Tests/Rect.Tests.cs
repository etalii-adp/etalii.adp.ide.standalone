using Xunit;

namespace EtAlii.Adp.Layout.Tests;

public class RectTests
{
    [Fact]
    public void TheDerivedEdges_FollowThePositionAndSize()
    {
        // Arrange.
        var rect = new Rect(10, 20, 30, 40);

        // Act and assert.
        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.Equal(25, rect.CenterX);
        Assert.Equal(40, rect.CenterY);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(10, 0, false)]
    [InlineData(0, 10, false)]
    [InlineData(-10, 0, false)]
    [InlineData(9.5, 9.5, true)]
    public void Overlaps_OnlyWhereTheyShareArea(double x, double y, bool expected)
    {
        // Arrange: two 10 by 10 squares; touching edges share no area.
        var first = new Rect(0, 0, 10, 10);
        var second = new Rect(x, y, 10, 10);

        // Act and assert, both ways round.
        Assert.Equal(expected, first.Overlaps(second));
        Assert.Equal(expected, second.Overlaps(first));
    }
}
