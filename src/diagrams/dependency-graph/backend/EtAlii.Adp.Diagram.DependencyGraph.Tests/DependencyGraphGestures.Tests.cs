using System.Globalization;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The two synthetic ids a canvas gesture travels through: a placement, and a finished relation.
/// </summary>
/// <remarks>
/// Worth their own file here in a way they were not in the timeline, because the placement's
/// payload changed meaning in the fork - it carries a canvas coordinate rather than seconds
/// since the epoch - and a coordinate can be fractional and negative where epoch seconds in
/// practice were neither.
/// </remarks>
public class DependencyGraphGesturesTests
{
    [Theory]
    [InlineData(0d, 0)]
    [InlineData(240d, 3)]
    [InlineData(-180.5d, -2)]
    [InlineData(412.25d, 12)]
    public void APlacement_RoundTripsThroughItsId(double x, int row)
    {
        // Act.
        var parsed = DependencyGraphNewPlacement.TryParse(
            DependencyGraphNewPlacement.IdFor(x, row), out var readX, out var readRow);

        // Assert.
        Assert.True(parsed);
        Assert.Equal(x, readX);
        Assert.Equal(row, readRow);
    }

    [Fact]
    public void APlacementIsWrittenAndReadInvariantly()
    {
        // Arrange.
        // A comma decimal separator would put a second comma in an id whose fields are separated
        // by one - so the round trip would not merely lose precision, it would parse as garbage.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            // Act.
            var id = DependencyGraphNewPlacement.IdFor(412.5d, 2);

            // Assert.
            Assert.Equal("new:412.5,2", id);
            Assert.True(DependencyGraphNewPlacement.TryParse(id, out var x, out var row));
            Assert.Equal(412.5d, x);
            Assert.Equal(2, row);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aaa")]
    [InlineData("new:")]
    [InlineData("new:240")]
    [InlineData("new:240,3,4")]
    [InlineData("new:sideways,3")]
    [InlineData("new:240,sideways")]
    public void AnythingElse_IsNotAPlacement(string? candidate)
    {
        // Act & assert.
        // An ordinary element id must never be mistaken for a placement: that would turn a
        // selection into a creation.
        Assert.False(DependencyGraphNewPlacement.TryParse(candidate, out _, out _));
    }

    [Fact]
    public void ARelationGesture_CarriesBothEndsInOrder()
    {
        // Act.
        // Which end is which is the type's whole meaning, so the id preserves the order the
        // gesture had rather than sorting or normalising it.
        var id = DependencyGraphRelationGesture.IdFor("aaa", "bbb");

        // Assert.
        Assert.True(DependencyGraphRelationGesture.TryParse(id, out var from, out var to));
        Assert.Equal("aaa", from);
        Assert.Equal("bbb", to);
    }

    [Fact]
    public void ARelationGestureTargetingAPlacement_SplitsAtTheFirstSeparator()
    {
        // Arrange.
        // A placement contains a comma and a colon but no arrow, so the split is unambiguous -
        // which is what lets a drag onto empty canvas travel through the same one-id channel.
        var placement = DependencyGraphNewPlacement.IdFor(-180.5d, 4);

        // Act.
        var id = DependencyGraphRelationGesture.IdFor("aaa", placement);

        // Assert.
        Assert.True(DependencyGraphRelationGesture.TryParse(id, out var from, out var to));
        Assert.Equal("aaa", from);
        Assert.Equal(placement, to);
        Assert.True(DependencyGraphNewPlacement.TryParse(to, out var x, out var row));
        Assert.Equal(-180.5d, x);
        Assert.Equal(4, row);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("aaa")]
    [InlineData("rel:")]
    [InlineData("rel:aaa")]
    [InlineData("rel:->bbb")]
    [InlineData("rel:aaa->")]
    public void AnythingElse_IsNotARelationGesture(string? candidate)
    {
        // Act & assert.
        Assert.False(DependencyGraphRelationGesture.TryParse(candidate, out _, out _));
    }
}
