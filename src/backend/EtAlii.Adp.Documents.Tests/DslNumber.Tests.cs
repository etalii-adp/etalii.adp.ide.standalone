using System.Globalization;
using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>The one number format of every module's documents and property rows (code-structure T135, FR-047).</summary>
public class DslNumberTests
{
    [Theory]
    [InlineData(900d, "900")]
    [InlineData(0d, "0")]
    [InlineData(412.5, "412.5")]
    [InlineData(220.125, "220.125")]
    [InlineData(123.456789, "123.4568")]
    [InlineData(-0.25, "-0.25")]
    public void ANumber_IsWrittenWithAtMostFourDecimals_AndAWholeOneWhole(double value, string expected)
    {
        // Act and assert.
        Assert.Equal(expected, DslNumber.Format(value));
    }

    [Fact]
    public void ANumber_IsWrittenTheSameUnderEveryCulture()
    {
        // Arrange.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            // Act and assert.
            Assert.Equal("412.5", DslNumber.Format(412.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NoNumber_IsEmpty()
    {
        // Arrange.
        double? none = null;
        double? some = 1.5;

        // Act and assert.
        Assert.Equal("", DslNumber.Format(none));
        Assert.Equal("1.5", DslNumber.Format(some));
    }
}
