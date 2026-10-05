using System.Globalization;
using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>
/// <c>string(double)</c> as cel-go writes it (Go's <c>%g</c>), pinned over the values the bundled
/// definitions format - <c>atText</c> and <c>sizeText</c> round to hundredths, note sizes and
/// boundary fractions stay well under a million - and at the edges where .NET's own round-trip text
/// differs.
/// </summary>
public class DoubleTextTests
{
    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(-0.0, "-0")]
    [InlineData(1.0, "1")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(0.25, "0.25")]
    [InlineData(0.1, "0.1")]
    [InlineData(0.33, "0.33")]
    [InlineData(64.0, "64")]
    [InlineData(160.0, "160")]
    [InlineData(123.45, "123.45")]
    [InlineData(999999.0, "999999")]
    [InlineData(999999.99, "999999.99")]
    [InlineData(1.0 / 3.0, "0.3333333333333333")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(-0.0001, "-0.0001")]
    // Where Go's %g leaves decimal notation and .NET's "R" does not, or differs in spelling.
    [InlineData(1000000.0, "1e+06")]
    [InlineData(1234567.0, "1.234567e+06")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(0.000015, "1.5e-05")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1.5e-7, "1.5e-07")]
    [InlineData(-1e100, "-1e+100")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308")]
    [InlineData(double.Epsilon, "5e-324")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "+Inf")]
    [InlineData(double.NegativeInfinity, "-Inf")]
    public void ADoubleIsWrittenAsCelGoWritesIt(double value, string expected)
    {
        // Act.
        var text = Evaluate.Expression("string(d)", new Dictionary<string, object?> { ["d"] = value });

        // Assert.
        Assert.Equal(expected, text);
    }

    [Fact]
    public void EveryHundredthBelowAMillionIsItsShortestDecimal()
    {
        // Arrange: the range atText and sizeText reach, sampled densely near zero and sparsely above.
        var program = CelEnvironment.Standard().DeclareVariable("a").Compile("string(math.round(a * 100.0) / 100.0)");
        var wrong = new List<string>();

        // Act.
        foreach (var hundredths in Enumerable.Range(0, 20_000).Concat(Enumerable.Range(0, 2_000).Select(i => i * 49_999)))
        {
            var value = hundredths / 100.0;
            var expected = value.ToString("0.##", CultureInfo.InvariantCulture);
            var actual = program.Evaluate(new Dictionary<string, object?> { ["a"] = value });
            if (!Equals(expected, actual)) wrong.Add($"{value}: {actual}, not {expected}");
        }

        // Assert.
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("1e3", 1000.0)]
    [InlineData("1.5e-3", 0.0015)]
    [InlineData("2E+2", 200.0)]
    public void ADoubleLiteralMayHaveAnExponent(string expression, double expected)
    {
        // Act and assert.
        Assert.Equal(expected, Evaluate.Expression(expression));
    }

    [Fact]
    public void AnIntLiteralOutOfRangeIsRefused()
    {
        // Act.
        var refused = Assert.Throws<CelException>(() => CelEnvironment.Standard().Compile("9223372036854775808"));

        // Assert.
        Assert.Equal("The int 9223372036854775808 is out of range, in '9223372036854775808'.", refused.Message);
    }
}
