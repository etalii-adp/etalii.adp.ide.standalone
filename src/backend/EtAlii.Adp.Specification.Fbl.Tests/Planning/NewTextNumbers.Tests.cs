using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Planning;

/// <summary>
/// FBL §6.3, numbers: "With <c>{decimals: n}</c>, the number is rounded to <c>n</c> decimals, halves away from
/// zero, and trailing zeros and a trailing point are dropped." A number is the decimal it is written as, its
/// shortest form, so a value typed as a half rounds as one even where the nearest double lies just below it, as
/// the TypeScript implementation rounds it (finding B19 of functional-decomposition-graph-disl-fbl).
/// </summary>
public class NewTextNumbersTests
{
    [Theory]
    [InlineData(200.00005, 4, "200.0001")]
    [InlineData(-200.00005, 4, "-200.0001")]
    [InlineData(1.005, 2, "1.01")]
    [InlineData(0.125, 2, "0.13")]
    [InlineData(120.5, 3, "120.5")]
    [InlineData(640.0, 3, "640")]
    [InlineData(200.00004, 4, "200")]
    [InlineData(-4E-05, 4, "0")]
    [InlineData(-5E-05, 4, "-0.0001")]
    public void ANumberWithDecimalsRoundsItsWrittenDecimalHalvesAwayFromZero(double value, int decimals, string written)
    {
        // Act.
        var text = NewText.Number(value, decimals);

        // Assert.
        Assert.Equal(written, text);
    }
}
