using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The row-to-y pair, tested hardest at the midpoint between two rows - where an off-by-one is
/// the mistake to expect, per the tasks document's own reliability note.
/// </summary>
public class TimelineRowsTests
{
    [Theory]
    [InlineData(-12)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void ARow_RoundTripsThroughY(int row)
    {
        // Act & assert.
        Assert.Equal(row, TimelineRows.ToNearestRow(TimelineRows.ToY(row)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void TheExactMidpoint_ResolvesTheSameWayBetweenEveryPairOfRows(int lower)
    {
        // Arrange.
        // Banker's rounding would send the 0/1 midpoint to 0 but the 1/2 midpoint to 2, so a drag
        // to the boundary would snap down on some rows and up on others - flaky to a user, and
        // invisible to any test that samples a single pair.
        var midpoint = TimelineRows.ToY(lower) + TimelineRows.Height / 2;

        // Act.
        var resolved = TimelineRows.ToNearestRow(midpoint);

        // Assert.
        var expected = lower >= 0 ? lower + 1 : lower;
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void JustPastTheMidpoint_SnapsToTheFarRow_AndJustShortSnapsBack()
    {
        // Arrange.
        var midpoint = TimelineRows.Height / 2;

        // Act & assert.
        Assert.Equal(1, TimelineRows.ToNearestRow(midpoint + 0.01));
        Assert.Equal(0, TimelineRows.ToNearestRow(midpoint - 0.01));
    }

    [Fact]
    public void AnywhereWithinARowsOwnHalf_SnapsToThatRow()
    {
        // Arrange & act & assert.
        // The whole band around a row belongs to it, not just its centre line.
        Assert.Equal(3, TimelineRows.ToNearestRow(TimelineRows.ToY(3) - TimelineRows.Height * 0.49));
        Assert.Equal(3, TimelineRows.ToNearestRow(TimelineRows.ToY(3) + TimelineRows.Height * 0.49));
    }

    [Fact]
    public void ANegativeY_SnapsToANegativeRow()
    {
        // Act & assert.
        // Negative rows are as valid as positive ones (Requirement 3.5), and rounding toward zero
        // would treat the -0.5 band asymmetrically.
        Assert.Equal(-1, TimelineRows.ToNearestRow(-TimelineRows.Height));
        Assert.Equal(-1, TimelineRows.ToNearestRow(-TimelineRows.Height * 0.6));
    }
}
