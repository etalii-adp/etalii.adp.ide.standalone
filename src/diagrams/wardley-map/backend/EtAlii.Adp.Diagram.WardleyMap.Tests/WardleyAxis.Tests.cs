using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyAxisTests
{
    [Fact]
    public void ToPoint_PutsAHighlyVisibleGenesisComponentAtTopLeft()
    {
        // Arrange. The single assertion that catches both easy mistakes at once: a swapped pair
        // reads the map sideways, a missing inversion turns it upside down, and both still
        // render perfectly well.
        var coordinate = new WardleyCoordinate(Visibility: 0.9d, Maturity: 0.1d);

        // Act.
        var (x, y) = WardleyAxis.ToPoint(coordinate);

        // Assert. Genesis is left, the user need is top.
        Assert.Equal(0.1d, x);
        Assert.Equal(0.09999999999999998d, y, 10);
    }

    [Fact]
    public void ToPoint_PutsAnInvisibleCommodityComponentAtBottomRight()
    {
        // Act.
        var (x, y) = WardleyAxis.ToPoint(new WardleyCoordinate(Visibility: 0.05d, Maturity: 0.95d));

        // Assert.
        Assert.Equal(0.95d, x);
        Assert.Equal(0.95d, y, 10);
    }

    [Fact]
    public void ToPoint_PutsTheAnchorAtTheTop()
    {
        // Act. An anchor is the user need, so visibility 1 - and the top of the canvas is y=0.
        var (_, y) = WardleyAxis.ToPoint(new WardleyCoordinate(Visibility: 1d, Maturity: 0.5d));

        // Assert.
        Assert.Equal(0d, y);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(1d, 1d)]
    [InlineData(0.79d, 0.61d)]
    [InlineData(0.5d, 0.5d)]
    [InlineData(0.175d, 0.7d)]
    public void ToCoordinate_InvertsToPointExactly(double visibility, double maturity)
    {
        // Arrange.
        var original = new WardleyCoordinate(visibility, maturity);

        // Act.
        var (x, y) = WardleyAxis.ToPoint(original);
        var round = WardleyAxis.ToCoordinate(x, y);

        // Assert. A drag reads a point back into the document, so a lossy pair would move
        // components by a rounding error every time one was touched.
        Assert.Equal(original.Visibility, round.Visibility, 10);
        Assert.Equal(original.Maturity, round.Maturity, 10);
    }

    [Fact]
    public void Clamp_HoldsACoordinateInsideTheBoundedSpace()
    {
        // Act. Requirement 7.3 - a component cannot be more evolved than commodity.
        var clamped = WardleyAxis.Clamp(new WardleyCoordinate(Visibility: 1.4d, Maturity: -0.3d));

        // Assert.
        Assert.Equal(1d, clamped.Visibility);
        Assert.Equal(0d, clamped.Maturity);
    }

    [Fact]
    public void Clamp_LeavesACoordinateThatIsAlreadyInsideAlone()
    {
        // Arrange.
        var coordinate = new WardleyCoordinate(0.79d, 0.61d);

        // Act.
        var clamped = WardleyAxis.Clamp(coordinate);

        // Assert.
        Assert.Equal(coordinate, clamped);
    }

    [Fact]
    public void ToPoint_AndTheEvolutionBands_AgreeOnWhereGenesisEnds()
    {
        // Arrange. The bands are drawn on the x axis, so the boundary between Genesis and
        // Custom Built must land at x = 0.175 - if the axes were transposed the bands would be
        // horizontal and the whole map would be a right angle out.
        var onTheBoundary = new WardleyCoordinate(Visibility: 0.5d, Maturity: WardleyEvolution.CustomBuilt);

        // Act.
        var (x, _) = WardleyAxis.ToPoint(onTheBoundary);

        // Assert.
        Assert.Equal(WardleyEvolution.CustomBuilt, x);
        Assert.Equal("Custom Built", WardleyEvolution.StageOf(x).Label);
    }
}
