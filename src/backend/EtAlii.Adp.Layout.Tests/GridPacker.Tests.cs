using Xunit;

namespace EtAlii.Adp.Layout.Tests;

public class GridPackerTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 200, 0)]
    [InlineData(3, 0, 50)]
    [InlineData(7, 100, 100)]
    public void Cell_FillsTheGridRowByRow(int index, double x, double y)
    {
        // Act: three to a row, cells 100 wide and 50 tall.
        var cell = GridPacker.Cell(index, 3, 100, 50);

        // Assert.
        Assert.Equal((x, y), cell);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public void Rows_CountsAPartFilledLastRow(int count, int rows)
    {
        // Act.
        var actual = GridPacker.Rows(count, 3);

        // Assert.
        Assert.Equal(rows, actual);
    }

    [Fact]
    public void Pack_PutsEachItemBesideTheOneBefore_AndEachRowBelowTheTallestAbove()
    {
        // Arrange: two to a row; the first row's tallest item is 30, the second row holds one item.
        (double, double)[] sizes = [(10, 20), (40, 30), (50, 5)];

        // Act.
        var grid = GridPacker.Pack(sizes, 2, gapX: 5, gapY: 7);

        // Assert.
        Assert.Equal([new Rect(0, 0, 10, 20), new Rect(15, 0, 40, 30), new Rect(0, 37, 50, 5)], grid.Cells);
        Assert.Equal(55, grid.Width);
        Assert.Equal(42, grid.Height);
    }

    [Fact]
    public void Pack_OfNothing_TakesNoRoom()
    {
        // Act.
        var grid = GridPacker.Pack([], 3, 10, 10);

        // Assert.
        Assert.Empty(grid.Cells);
        Assert.Equal(0, grid.Width);
        Assert.Equal(0, grid.Height);
    }
}
