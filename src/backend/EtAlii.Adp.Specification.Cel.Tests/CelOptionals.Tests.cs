using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>Optional values: <c>x.?f</c>, <c>m[?k]</c>, <c>optional.of</c>/<c>none</c> and their methods, after cel-spec's optionals tests.</summary>
public class CelOptionalsTests
{
    [Theory]
    // cel-spec optionals.textproto, as far as this subset reaches.
    [InlineData("optional.of(1).hasValue()", "true")]
    [InlineData("optional.none().hasValue()", "false")]
    [InlineData("optional.of(1).value()", "1")]
    [InlineData("optional.none().value()", "error: optional.none() has no value.")]
    [InlineData("optional.none().orValue(42)", "42")]
    [InlineData("optional.of('a').orValue('b')", "a")]
    [InlineData("optional.none().or(optional.of(2))", "optional.of(2)")]
    [InlineData("optional.of(1).or(optional.of(2))", "optional.of(1)")]
    [InlineData("optional.none() == optional.none()", "true")]
    [InlineData("optional.of(1) == optional.of(1)", "true")]
    [InlineData("optional.of(1) == optional.none()", "false")]
    [InlineData("{'key': 'test'}.?key", "optional.of(test)")]
    [InlineData("{'key': 'test'}.?missing", "optional.none()")]
    [InlineData("{'key': 'test'}.?missing.orValue('default')", "default")]
    [InlineData("{'k': {'n': 1}}.?k.n", "optional.of(1)")]
    [InlineData("{'k': {'n': 1}}.?x.n", "optional.none()")]
    [InlineData("{'k': {'n': 1}}.?k.?m.orValue(0)", "0")]
    [InlineData("{'key': 'test'}[?'key']", "optional.of(test)")]
    [InlineData("{'key': 'test'}[?'other'].hasValue()", "false")]
    [InlineData("[1, 2, 3][?1]", "optional.of(2)")]
    [InlineData("[1, 2, 3][?5].orValue(-1)", "-1")]
    [InlineData("[[1]][?0][0]", "optional.of(1)")]
    public void AnOptionalExpressionEvaluates(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Theory]
    [InlineData("self.?name.orValue('')", "AI")]
    [InlineData("self.?peakEnd.orValue(-1)", "-1")]
    [InlineData("self.?nothing.hasValue()", "false")]
    [InlineData("self.?start.value() + 1", "24001")]
    public void AnOptionalSelectionOnAnObjectFollowsHas(string expression, string expected)
    {
        // Arrange.
        var trend = new HostObject("Trend", new Dictionary<string, object?> { ["name"] = "AI", ["start"] = 24000L, ["peakEnd"] = HostObject.Absent });

        // Act.
        var value = Evaluate.Expression(expression, new Dictionary<string, object?> { ["self"] = trend });

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Theory]
    [InlineData("a ? b : c", "1")]
    [InlineData("a ? m.?k.orValue(0) : 2", "5")]
    [InlineData("!a ? 1 : m[?'k'].orValue(0)", "5")]
    public void TheOptionalTokensDoNotDisturbTheConditional(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression, new Dictionary<string, object?> { ["a"] = true, ["b"] = 1L, ["c"] = 2L, ["m"] = new CelMap { ["k"] = 5L } });

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Fact]
    public void HasRefusesAnOptionalSelection()
    {
        // Act.
        var refused = Assert.Throws<CelException>(() => CelEnvironment.Standard().DeclareVariable("m").Compile("has(m.?k)"));

        // Assert.
        Assert.Equal("has() needs a field selection such as has(entry.end).", refused.Message);
    }
}
