using System.Text.Json;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 15: the time scale and the phase boundaries, computed once (Requirement 3).
/// </summary>
public class GhgPhasesTests
{
    private static int M(int year, int month = 1) => GhgScale.MonthIndex(year, month);

    private static GhgTrend Trend(int start, int stop, int phases, params int?[] dragged) =>
        new("t", "T", start, stop, 0, phases, dragged.Length == 0 ? [null, null, null] : dragged, [], "", default);

    /// <summary>Requirement 3.3: with nothing dragged, the visible phases divide the span evenly.</summary>
    [Fact]
    public void WithNothingDragged_ThePhasesAreEven()
    {
        Assert.Equal([M(1910), M(1920), M(1930)], GhgPhases.BoundariesOf(Trend(M(1900), M(1940), 4)));
        Assert.Equal([M(1920)], GhgPhases.BoundariesOf(Trend(M(1900), M(1940), 2)));
        Assert.Empty(GhgPhases.BoundariesOf(Trend(M(1900), M(1940), 1)));
    }

    /// <summary>
    /// Requirement 3.4: one dragged boundary stays where it was put, and the others spread evenly
    /// between it and the trend's ends - the case even spreading that ignores dragged neighbours fails.
    /// </summary>
    [Fact]
    public void OneDragged_StaysAndTheOthersSpreadBetweenItAndTheEnds()
    {
        // Trough ends at 1904: the Peak takes half of 1900-1904, the Slope half of 1904-1940.
        var boundaries = GhgPhases.BoundariesOf(Trend(M(1900), M(1940), 4, null, M(1904), null));

        Assert.Equal([M(1902), M(1904), M(1922)], boundaries);
    }

    [Fact]
    public void TwoDragged_StayAndTheThirdSpreadsBetweenTheNearest()
    {
        var boundaries = GhgPhases.BoundariesOf(Trend(M(1900), M(1940), 4, M(1901), null, M(1911)));

        Assert.Equal([M(1901), M(1906), M(1911)], boundaries);
    }

    /// <summary>
    /// Requirement 3.4: a boundary dragged beyond the last visible phase is kept, not drawn, and is no
    /// neighbour for the ones that are drawn.
    /// </summary>
    [Fact]
    public void ADraggedBoundaryBeyondTheLastPhase_IsNotDrawnAndNotANeighbour()
    {
        var trend = Trend(M(1900), M(1940), 2, null, M(1904), null);

        Assert.Equal([M(1920)], GhgPhases.BoundariesOf(trend));
        Assert.Equal([0.5], GhgPhases.FractionsOf(trend));

        // And applies again when its phase is shown.
        Assert.Equal([M(1902), M(1904)], GhgPhases.BoundariesOf(trend with { Phases = 3 }));
    }

    /// <summary>Requirement 3.5: a move keeps the dragged boundaries exactly.</summary>
    [Fact]
    public void AMove_ShiftsEveryStoredBoundaryExactly()
    {
        Assert.Equal([null, M(1814, 3), M(1830)], GhgPhases.Moved([null, M(1804, 3), M(1820)], 120));
    }

    /// <summary>
    /// Requirement 3.5: a resize scales each dragged boundary with the span and snaps it to a month -
    /// the case a resize that keeps absolute boundaries fails, because the boundary then falls outside.
    /// </summary>
    [Fact]
    public void AResize_ScalesTheDraggedBoundaries_AndKeepsThemInsideTheSpan()
    {
        // 1900-1940 with the Trough ending a quarter of the way in, shrunk to 1900-1920.
        var dragged = GhgPhases.Rescaled([null, M(1910), null], M(1900), M(1940), M(1900), M(1920), 4);

        Assert.Equal([null, M(1905), null], dragged);

        // A left-edge resize: the offset is from the NEW start.
        var fromTheLeft = GhgPhases.Rescaled([null, M(1910), null], M(1900), M(1940), M(1920), M(1940), 4);
        Assert.Equal([null, M(1925), null], fromTheLeft);
    }

    [Fact]
    public void AResize_KeepsEveryPhaseAtLeastAMonth()
    {
        // Four phases squeezed into four months: each boundary must land a month apart.
        var dragged = GhgPhases.Rescaled([M(1900, 2), M(1900, 3), M(1900, 4)], M(1900), M(1940), M(1900), M(1900, 5), 4);
        var trend = Trend(M(1900), M(1900, 5), 4, [.. dragged]);

        var points = new[] { M(1900) }.Concat(GhgPhases.BoundariesOf(trend)).Append(M(1900, 5)).ToArray();
        Assert.All(points.Zip(points.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 1, $"{pair.First} to {pair.Second}"));
    }

    /// <summary>Requirement 4.6: a boundary drag is clamped so no phase, even or dragged, is under a month.</summary>
    [Fact]
    public void ABoundaryDrag_IsClampedAMonthShortOfItsNeighbours()
    {
        // Dragging the Peak's end past the stop leaves room for the Trough, Slope and Plateau.
        var dragged = GhgPhases.WithBoundary([null, null, null], 0, M(1950), M(1900), M(1940), 4);

        Assert.Equal([M(1939, 10), null, null], dragged);
        Assert.Equal([M(1939, 10), M(1939, 11), M(1939, 12)], GhgPhases.BoundariesOf(Trend(M(1900), M(1940), 4, [.. dragged])));
    }

    /// <summary>
    /// The scale, against the one checked-in fixture the client asserts too: a trend's left and right
    /// edges are its start and stop, and a row's top and middle are where the design puts them.
    /// </summary>
    [Fact]
    public void TheScale_AgreesWithTheSharedFixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(GhgModuleFiles.ScaleFixture));
        var root = fixture.RootElement;

        Assert.Equal(GhgScale.UnitsPerMonth, root.GetProperty("unitsPerMonth").GetInt32());
        Assert.Equal(GhgScale.OriginDate, root.GetProperty("origin").GetString());
        Assert.Equal(GhgScale.TrendHeight, root.GetProperty("trendHeight").GetDouble());
        Assert.Equal(GhgScale.RowStep, root.GetProperty("rowStep").GetDouble());

        var mapper = new GhgElementMapper();
        foreach (var month in root.GetProperty("months").EnumerateArray())
        {
            var date = month.GetProperty("date").GetString()!;
            var x = month.GetProperty("x").GetDouble();
            var index = GhgScale.ParseMonth(date)!.Value;

            Assert.Equal(x, GhgScale.XOf(index));
            Assert.Equal(date, GhgScale.FormatMonth(GhgScale.NearestMonthAt(x)));
            Assert.Equal(date, GhgScale.FormatMonth(GhgScale.MonthContaining(x + 1.5)));

            // Through the mapper: a trend starting here has its left edge at x, and one stopping here
            // has its right edge at x.
            var (startingX, _, startingWidth) = Drawn(mapper, Trend(index, index + 12, 4));
            var (stoppingX, _, stoppingWidth) = Drawn(mapper, Trend(index - 12, index, 4));
            Assert.Equal(x, startingX - (startingWidth / 2));
            Assert.Equal(x, stoppingX + (stoppingWidth / 2));
        }

        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var number = row.GetProperty("row").GetInt32();
            Assert.Equal(row.GetProperty("top").GetDouble(), GhgScale.TopOf(number));
            Assert.Equal(number, GhgScale.RowAtTop(row.GetProperty("top").GetDouble()));
            Assert.Equal(number, GhgScale.RowAtMiddle(row.GetProperty("middle").GetDouble()));
            Assert.Equal(row.GetProperty("middle").GetDouble(), Drawn(mapper, Trend(M(1900), M(1901), 4) with { Row = number }).Y);
        }
    }

    /// <summary>
    /// backend-centralization R9.1: a row is found through the one shared rounding rule, so a top exactly
    /// between two rows rounds away from zero. 2.5 and -2.5 rows are the halves where rounding half to
    /// even (.NET's default) gives 2 and -2, and 0.5 and -0.5 are where it gives 0 - so a copy that
    /// dropped the midpoint rule lands a row short here.
    /// </summary>
    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    [InlineData(2.5, 3)]
    [InlineData(-2.5, -3)]
    public void ATopExactlyBetweenRows_RoundsAwayFromZero(double rows, int expected)
    {
        Assert.Equal(expected, GhgScale.RowAtTop(rows * GhgScale.RowStep));
        Assert.Equal(expected, GhgScale.RowAtMiddle((rows * GhgScale.RowStep) + (GhgScale.TrendHeight / 2)));
    }

    /// <summary>
    /// backend-centralization R9.1: this module's row is the shared rule's row at every quarter-row from
    /// -5 to 5, halves included, so the two cannot drift apart.
    /// </summary>
    [Fact]
    public void TheRowAtATop_IsTheSharedRowRounding()
    {
        for (var quarter = -20; quarter <= 20; quarter++)
        {
            var top = quarter * GhgScale.RowStep / 4;
            Assert.Equal(RowRounding.ToNearestRow(top, GhgScale.RowStep), GhgScale.RowAtTop(top));
        }
    }

    [Theory]
    [InlineData("1900-13")]
    [InlineData("1900-00")]
    [InlineData("1900")]
    [InlineData("19000-01")]
    [InlineData("1900-1")]
    public void AMonthThatIsNotYyyyMm_DoesNotParse(string text) => Assert.Null(GhgScale.ParseMonth(text));

    private static (double X, double Y, double Width) Drawn(GhgElementMapper mapper, GhgTrend trend)
    {
        var element = Assert.Single(mapper.Visible(new GhgModel([trend], [], [], 1), DiagramViewport.Unbounded));
        var payload = GhgTrendPayload.Parser.ParseFrom(element.Payload.ToArray());
        return (element.X, element.Y, payload.Width);
    }
}
