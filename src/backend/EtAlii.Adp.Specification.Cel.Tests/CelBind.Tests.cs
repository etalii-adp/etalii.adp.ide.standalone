using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The <c>cel.bind</c> macro, after cel-spec's bindings_ext tests.</summary>
public class CelBindTests
{
    [Theory]
    // cel-spec bindings_ext.textproto.
    [InlineData("cel.bind(t, true, t)", "true")]
    [InlineData("cel.bind(a, 'hello', cel.bind(b, 'world', a + b + b + a))", "helloworldworldhello")]
    [InlineData("cel.bind(valid_elems, [1, 2, 3], [3, 4, 5].exists(e, e in valid_elems))", "true")]
    [InlineData("cel.bind(valid_elems, [1, 2, 3], ![4, 5].exists(e, e in valid_elems))", "true")]
    // Shadowing and use inside a comprehension.
    [InlineData("cel.bind(x, 1, cel.bind(x, x + 1, x))", "2")]
    [InlineData("cel.bind(n, 10, [1, 2].map(i, i * n))", "[10,20]")]
    [InlineData("[1, 2].map(i, cel.bind(sq, i * i, sq + sq))", "[2,8]")]
    public void ABindingIsVisibleInItsBody(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Fact]
    public void ABindingIsNotVisibleOutsideItsBody()
    {
        // Act.
        var refused = Record.Exception(() => CelEnvironment.Standard().DeclareVariable("self").Compile("cel.bind(x, 1, x) + x"));

        // Assert.
        Assert.Equal("'x' is not a variable here; available: self.", refused.Message);
    }

    [Fact]
    public void TheInitIsEvaluatedOnce()
    {
        // Arrange.
        var calls = 0;
        var environment = CelEnvironment.Standard().AddFunction(CelFunction.Global("tick", 0, _ => (long)++calls));

        // Act.
        var value = Evaluate.Expression("cel.bind(t, tick(), t + t + t)", environment: environment);

        // Assert.
        Assert.Equal(3L, value);
        Assert.Equal(1, calls);
    }
}
