using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The time-to-x conversion, which the design names as the module's most error-prone surface.
/// </summary>
/// <remarks>
/// The round trips run over a spread of instants rather than one convenient sample, because the
/// mistakes this pair can make - an offset applied twice, a truncation instead of a rounding, a
/// date promoted to a date-time - each survive a single well-chosen example and fail somewhere
/// else in the range.
/// </remarks>
public class TimelineScaleTests
{
    public static TheoryData<string, TimelinePrecision> AcrossTheRange() => new()
    {
        { "1970-01-01T00:00:00", TimelinePrecision.DateTime },
        { "1969-07-20T20:17:40", TimelinePrecision.DateTime }, // before the epoch: negative seconds
        { "2026-01-05", TimelinePrecision.Date },
        { "2026-02-16T14:00:00", TimelinePrecision.DateTime },
        { "2026-12-31T23:59:59", TimelinePrecision.DateTime },
        { "2100-06-15", TimelinePrecision.Date },
        { "1900-03-01", TimelinePrecision.Date },
    };

    [Theory]
    [MemberData(nameof(AcrossTheRange))]
    public void AnInstant_RoundTripsThroughSeconds(string text, TimelinePrecision precision)
    {
        // Arrange.
        var value = DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

        // Act.
        var back = TimelineScale.ToTime(TimelineScale.ToSeconds(value), precision);

        // Assert.
        Assert.Equal(value, back);
    }

    [Theory]
    [MemberData(nameof(AcrossTheRange))]
    public void AnInstant_RoundTripsThroughItsText(string text, TimelinePrecision precision)
    {
        // Arrange.
        var value = DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

        // Act & assert.
        // The writer splices ToText's output straight into the file, so the text form is as much
        // a round trip as the numeric one.
        Assert.Equal(text, TimelineScale.ToText(value, precision));
    }

    [Fact]
    public void TheEpochIsZero()
    {
        // Assert.
        // Pins the origin: a constant offset error anywhere in the pair shows up here first.
        Assert.Equal(0d, TimelineScale.ToSeconds(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ADateOnlyValue_ComesBackDateOnly_AfterADrag()
    {
        // Arrange.
        // The scenario the precision exists for: a drag shifts a date-only element by an amount
        // that is not a whole number of days, and the landing value must still be a date.
        var begin = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var dragged = TimelineScale.ToSeconds(begin) + 3.7 * 60 * 60; // +3.7 hours

        // Act.
        var landed = TimelineScale.ToTime(dragged, TimelinePrecision.Date);

        // Assert.
        Assert.Equal(TimeSpan.Zero, landed.TimeOfDay);
        Assert.Equal("2026-01-05", TimelineScale.ToText(landed, TimelinePrecision.Date));
    }

    [Fact]
    public void AFractionalSecond_RoundsToTheFormatsFinestUnit()
    {
        // Arrange.
        var value = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        // Act.
        var landed = TimelineScale.ToTime(TimelineScale.ToSeconds(value) + 0.4, TimelinePrecision.DateTime);

        // Assert.
        // The format's finest unit is a second; a drag must not manufacture milliseconds the
        // author could never have written.
        Assert.Equal(value, landed);
    }

    [Fact]
    public void NoTimezoneEverAppears()
    {
        // Arrange & act.
        // Requirement 3.7: values at face value, so the same document draws identically on every
        // machine. An offset sneaking in would surface as ToText emitting a suffix.
        var value = TimelineScale.ToTime(TimelineScale.ToSeconds(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)), TimelinePrecision.DateTime);

        // Assert.
        Assert.Equal(TimeSpan.Zero, value.Offset);
        Assert.DoesNotContain("+", TimelineScale.ToText(value, TimelinePrecision.DateTime), StringComparison.Ordinal);
        Assert.DoesNotContain("Z", TimelineScale.ToText(value, TimelinePrecision.DateTime), StringComparison.Ordinal);
    }
}
