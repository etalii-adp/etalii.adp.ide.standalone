using EtAlii.Adp.Layout;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramViewportTests
{
    [Theory]
    [InlineData(50, 50, true)]
    [InlineData(100, 50, true)]
    [InlineData(-10, 50, true)]
    [InlineData(100.5, 50, false)]
    [InlineData(-10.5, 50, false)]
    [InlineData(50, 100.5, false)]
    [InlineData(50, -10.5, false)]
    public void Intersects_CountsABoxTheViewportMerelyTouches(double x, double y, bool expected)
    {
        // Arrange: a 100 by 100 viewport and a 10 by 10 box.
        var viewport = new DiagramViewport(0, 0, 100, 100);

        // Act and assert.
        Assert.Equal(expected, viewport.Intersects(new Rect(x, y, 10, 10)));
    }

    [Fact]
    public void TheUnboundedViewport_HasEveryBoxInView()
    {
        // Act and assert.
        Assert.True(DiagramViewport.Unbounded.Intersects(new Rect(-1e9, 1e9, 1, 1)));
    }
}
