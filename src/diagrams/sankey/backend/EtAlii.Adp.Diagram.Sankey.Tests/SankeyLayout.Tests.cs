using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The layout: columns from the flows, bars and bands on one scale, the document's order within a
/// column, and bands that leave and reach a bar without crossing.
/// </summary>
public sealed class SankeyLayoutTests
{
    private static SankeyModel Small() =>
        SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.skv"))));

    private static SankeyModel Parse(string text) => SankeyParser.Parse(LineDocument.Parse(text));

    [Fact]
    public void Columns_FollowTheFlows_FromTheSourcesRightwards()
    {
        // Act.
        var layout = SankeyLayout.Of(Small());

        // Assert.
        Assert.Equal([0, 0, 1, 2, 2], new[] { "a", "b", "m", "x", "y" }.Select(id => layout.Columns[id]));
        Assert.Equal(["a", "b"], layout.ColumnOrder[0]);
        Assert.Equal(["x", "y"], layout.ColumnOrder[2]);
        Assert.All(layout.Boxes, pair => Assert.Equal(SankeyLayout.LeftOf(layout.Columns[pair.Key]), pair.Value.X));
    }

    [Fact]
    public void ANodesValue_IsTheLargerOfWhatFlowsInAndOut()
    {
        // Act.
        var layout = SankeyLayout.Of(Parse("sankey: 1\nnodes:\n  - id: a\n  - id: b\n  - id: c\nflows:\n  - from: a\n    to: b\n    value: 5\n  - from: b\n    to: c\n    value: 3\n"));

        // Assert.
        Assert.Equal((5d, 5d, 3d), (layout.Values["a"], layout.Values["b"], layout.Values["c"]));
    }

    [Fact]
    public void BarsAndBands_ShareOneScale_AndTheFullestColumnFillsTheBaseHeight()
    {
        // Act.
        var layout = SankeyLayout.Of(Small());

        // Assert: every column carries 10, so 10 units fill the base height.
        Assert.Equal(SankeyGeometry.BaseHeight / 10, layout.Scale, 6);
        Assert.Equal(6 * layout.Scale, layout.Boxes["a"].Height, 2);
        Assert.Equal(4 * layout.Scale, layout.Boxes["b"].Height, 2);
        Assert.Equal(10 * layout.Scale, layout.Boxes["m"].Height, 2);
        Assert.Equal(7 * layout.Scale, layout.Bands["m->x"].Thickness, 2);
    }

    [Fact]
    public void TheThicknessSetting_ScalesEveryBarAndBand()
    {
        // Arrange.
        var thin = SankeyLayout.Of(Small());

        // Act.
        var thick = SankeyLayout.Of(Small() with { Settings = SankeySettings.Default with { Thickness = 1.5 } });

        // Assert.
        Assert.Equal(thin.Boxes["m"].Height * 1.5, thick.Boxes["m"].Height, 2);
        Assert.Equal(thin.Bands["a->m"].Thickness * 1.5, thick.Bands["a->m"].Thickness, 2);
    }

    [Fact]
    public void TheDocumentsOrder_IsTheOrderInEachColumn_AndNodesNeverCloserThanTheGap()
    {
        // Act.
        var layout = SankeyLayout.Of(Small());

        // Assert.
        foreach (var column in layout.ColumnOrder)
        {
            for (var i = 1; i < column.Count; i++)
            {
                var above = layout.Boxes[column[i - 1]];
                var below = layout.Boxes[column[i]];
                Assert.True(below.Y >= above.Bottom + SankeyGeometry.NodeGap - 0.01, $"{column[i]} is closer than the gap to {column[i - 1]}");
            }
        }
    }

    [Fact]
    public void BandsLeaveAndReachABar_StackedInTheOrderOfTheirOtherEnds()
    {
        // Act.
        var layout = SankeyLayout.Of(Small());

        // Assert: x is above y, so the band to x leaves the middle above the band to y - and they
        // fill the bar between them, the first from the top and the second to the bottom.
        var toX = layout.Bands["m->x"];
        var toY = layout.Bands["m->y"];
        Assert.True(toX.SourceAt < toY.SourceAt);
        Assert.Equal(0.35, toX.SourceAt, 3);
        Assert.Equal(0.85, toY.SourceAt, 3);
        Assert.Equal(0.3, layout.Bands["a->m"].TargetAt, 3);
        Assert.Equal(0.8, layout.Bands["b->m"].TargetAt, 3);
        Assert.Equal(0.5, toX.TargetAt, 3);
        Assert.All(layout.Bands.Values, band => Assert.False(band.Backward));
    }

    [Fact]
    public void ABandBetweenTwoSingleNodes_RunsLevel()
    {
        // Act: d starts centred in its column, below c, and is drawn towards b.
        var layout = SankeyLayout.Of(Parse(
            "sankey: 1\nnodes:\n  - id: a\n  - id: b\n  - id: c\n  - id: d\nflows:\n" +
            "  - from: a\n    to: c\n    value: 9\n  - from: b\n    to: d\n    value: 1\n"));

        // Assert: both bands are flat.
        foreach ((string from, string to) in new[] { ("a", "c"), ("b", "d") })
        {
            var band = layout.Bands[$"{from}->{to}"];
            var leaves = layout.Boxes[from].Y + (band.SourceAt * layout.Boxes[from].Height);
            var arrives = layout.Boxes[to].Y + (band.TargetAt * layout.Boxes[to].Height);
            Assert.InRange(Math.Abs(leaves - arrives), 0, 0.5);
        }
    }

    [Fact]
    public void AStatedColumn_Wins_AndACycleIsBrokenAtTheFlowThatClosesIt()
    {
        // Act.
        var layout = SankeyLayout.Of(Parse(
            "sankey: 1\nnodes:\n  - id: a\n  - id: b\n  - id: c\n  - id: lone\n    column: 4\nflows:\n" +
            "  - from: a\n    to: b\n    value: 2\n  - from: b\n    to: c\n    value: 2\n  - from: c\n    to: a\n    value: 1\n"));

        // Assert.
        Assert.Equal([0, 1, 2, 3], new[] { "a", "b", "c", "lone" }.Select(id => layout.Columns[id]));
        Assert.True(layout.Bands["c->a"].Backward);
        Assert.False(layout.Bands["a->b"].Backward);
    }

    [Fact]
    public void WhatCannotBeDrawn_IsLeftOut_AndTheRestIsLaidOut()
    {
        // Act.
        var layout = SankeyLayout.Of(Parse(
            "sankey: 1\nnodes:\n  - name: no id\n  - id: a\n  - id: a\n  - id: b\nflows:\n" +
            "  - from: a\n    to: ghost\n    value: 1\n  - from: a\n    to: a\n    value: 1\n  - from: a\n    to: b\n"));

        // Assert: one a, the hairline a->b with no value, and nothing for the rest.
        Assert.Equal(["a", "b"], layout.Nodes.Select(node => node.Id));
        Assert.Equal(["a->b"], layout.Flows.Select(flow => flow.Id));
        Assert.Equal(SankeyGeometry.MinimumThickness, layout.Bands["a->b"].Thickness);
        Assert.Equal(SankeyGeometry.MinimumNodeHeight, layout.Boxes["a"].Height);
    }

    [Fact]
    public void AnEmptyDocument_HasAnEmptyLayout()
    {
        // Act.
        var layout = SankeyLayout.Of(SankeyModel.Empty);

        // Assert.
        Assert.Empty(layout.Boxes);
        Assert.Empty(layout.ColumnOrder);
    }

    [Fact]
    public void TheLayout_IsComputedOncePerModel()
    {
        // Arrange.
        var model = Small();

        // Act + Assert.
        Assert.Same(SankeyLayout.Of(model), SankeyLayout.Of(model));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(150, 0)]
    [InlineData(200, 1)]
    [InlineData(-500, 0)]
    [InlineData(970, 3)]
    public void ADrop_IsInTheNearestColumn(double x, int column)
    {
        // Act + Assert.
        Assert.Equal(column, SankeyLayout.ColumnAt(x));
    }
}
