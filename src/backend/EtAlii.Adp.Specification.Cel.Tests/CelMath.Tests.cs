using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The math library, after cel-spec's math_ext tests; <c>math.round</c> sends halves away from zero.</summary>
public class CelMathTests
{
    [Theory]
    [InlineData("math.round(1.2)", "1")]
    [InlineData("math.round(1.5)", "2")]
    [InlineData("math.round(2.5)", "3")]
    [InlineData("math.round(-1.5)", "-2")]
    [InlineData("math.round(-2.5)", "-3")]
    [InlineData("math.round(0.5)", "1")]
    [InlineData("math.round(-0.4)", "-0")]
    [InlineData("math.floor(1.9)", "1")]
    [InlineData("math.floor(-1.2)", "-2")]
    [InlineData("math.ceil(1.2)", "2")]
    [InlineData("math.ceil(-1.9)", "-1")]
    [InlineData("math.trunc(-1.9)", "-1")]
    [InlineData("math.trunc(1.9)", "1")]
    [InlineData("math.abs(-1)", "1")]
    [InlineData("math.abs(1)", "1")]
    [InlineData("math.abs(-1.5)", "1.5")]
    [InlineData("math.greatest(1)", "1")]
    [InlineData("math.greatest(1, -2.0)", "1")]
    [InlineData("math.greatest(-1.5, 2, 1.25)", "2")]
    [InlineData("math.greatest([1, 5, 3])", "5")]
    [InlineData("math.least(1, -2.0)", "-2")]
    [InlineData("math.least([3.5, 1, 2])", "1")]
    [InlineData("math.least([])", "error: math.least() needs at least one number.")]
    [InlineData("math.greatest('a', 'b')", "error: A number was expected.")]
    public void AMathFunctionEvaluates(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value is double d ? CelValuesText(d) : value));
    }

    [Fact]
    public void RoundingAndTruncatingGiveDoubles_AbsKeepsTheType_AndTheExtremesKeepTheWinner()
    {
        // Act and assert.
        Assert.IsType<double>(Evaluate.Expression("math.round(2.5)"));
        Assert.IsType<double>(Evaluate.Expression("math.floor(2)"));
        Assert.IsType<long>(Evaluate.Expression("math.abs(-2)"));
        Assert.IsType<double>(Evaluate.Expression("math.abs(-2.0)"));
        Assert.IsType<long>(Evaluate.Expression("math.greatest(1, 0.5)"));
        Assert.IsType<double>(Evaluate.Expression("math.least(1, 0.5)"));
    }

    [Fact]
    public void AbsOfTheSmallestIntOverflows()
    {
        // Act.
        var value = Evaluate.Expression("math.abs(n)", new Dictionary<string, object?> { ["n"] = long.MinValue });

        // Assert.
        Assert.Equal("error: math.abs() overflows an int.", Evaluate.Text(value));
    }

    // string(double) is pinned on its own; here a double is spelled as CEL would.
    private static object? CelValuesText(double value) => Evaluate.Expression("string(d)", new Dictionary<string, object?> { ["d"] = value });
}
