using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>
/// Int arithmetic stays int. It did not: a negated int, and the difference and product of two ints,
/// came back as doubles, because C# types a conditional of long and double as double - so
/// <c>list[i - 1]</c> failed with "An int was expected".
/// </summary>
public class CelArithmeticTests
{
    [Theory]
    [InlineData("-1", -1L)]
    [InlineData("5 - 3", 2L)]
    [InlineData("5 * 3", 15L)]
    [InlineData("7 / 2", 3L)]
    [InlineData("7 % 2", 1L)]
    [InlineData("5 + 3", 8L)]
    [InlineData("[10, 20, 30][3 - 1]", 30L)]
    [InlineData("[10, 20, 30][-1 + 1]", 10L)]
    [InlineData("-1.5", -1.5)]
    [InlineData("5.0 - 3", 2.0)]
    public void AnIntOperationGivesAnInt_AndADoubleOneADouble(string expression, object expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, value);
        Assert.IsType(expected.GetType(), value);
    }
}

/// <summary>
/// An int operation that overflows is an error, as CEL makes it. It wrapped silently: the sum of
/// two large ints came back as a large negative one.
/// </summary>
public class CelOverflowTests
{
    [Theory]
    [InlineData("9223372036854775807 + 1")]
    [InlineData("-9223372036854775807 - 2")]
    [InlineData("4611686018427387904 * 2")]
    [InlineData("-(-9223372036854775807 - 1)")]
    [InlineData("(-9223372036854775807 - 1) / -1")]
    [InlineData("(-9223372036854775807 - 1) % -1")]
    public void AnIntOperationThatOverflows_IsAnError(string expression)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        var error = Assert.IsType<CelError>(value);
        Assert.Contains("overflow", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
