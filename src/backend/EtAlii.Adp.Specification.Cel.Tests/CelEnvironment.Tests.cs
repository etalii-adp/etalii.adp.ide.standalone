using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>What an environment lets an expression use, and how it refuses the rest.</summary>
public class CelEnvironmentTests
{
    [Fact]
    public void AnUndeclaredVariableIsRefusedNamingTheDeclaredOnes()
    {
        // Arrange.
        var environment = CelEnvironment.Standard().DeclareVariables("self", "diagram");

        // Act.
        var refused = Record.Exception(() => environment.Compile("self.name == other.name"));

        // Assert.
        Assert.IsType<CelException>(refused);
        Assert.Equal("'other' is not a variable here; available: self, diagram.", refused.Message);
    }

    [Fact]
    public void AnEnvironmentThatAllowsUndeclaredVariablesCompilesThem_AndEvaluationNamesTheOneWithNoValue()
    {
        // Arrange.
        var environment = CelEnvironment.Standard();
        environment.AllowsUndeclaredVariables = true;

        // Act.
        var program = environment.Compile("anything + 1");

        // Assert.
        Assert.Equal(2L, program.Evaluate(new Dictionary<string, object?> { ["anything"] = 1L }));
        Assert.Equal("error: 'anything' has no value.", Evaluate.Text(program.Evaluate(new Dictionary<string, object?>())));
    }

    [Theory]
    [InlineData("twice(4)", 8L)]
    [InlineData("4.twice()", 8L)]
    [InlineData("'ab'.twice()", "abab")]
    [InlineData("math2.square(3)", 9L)]
    public void ARegisteredFunctionIsCalledGloballyOrOnAReceiver(string expression, object expected)
    {
        // Arrange.
        var environment = CelEnvironment.Standard()
            .AddFunction(CelFunction.Global("twice", 1, a => Twice(a[0])))
            .AddFunction(CelFunction.Receiver("twice", 0, a => Twice(a[0])))
            .AddFunction(CelFunction.Global("math2.square", 1, a => (long)a[0]! * (long)a[0]!));

        // Act.
        var value = Evaluate.Expression(expression, environment: environment);

        // Assert.
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("nothing(1)", "The function 'nothing' is not supported by this CEL evaluator.")]
    [InlineData("1.nothing()", "The function 'nothing' is not supported by this CEL evaluator.")]
    [InlineData("twice(1, 2)", "The function 'twice' takes 1 argument, not 2, in 'twice(1, 2)'.")]
    [InlineData("pick()", "The function 'pick' takes 1 to 3 arguments, not 0, in 'pick()'.")]
    [InlineData("1.twice()", "The function 'twice' is not supported by this CEL evaluator.")]
    public void AnUnknownFunctionOrAWrongArityIsRefusedAtCompile(string expression, string message)
    {
        // Arrange.
        var environment = CelEnvironment.Standard()
            .AddFunction(CelFunction.Global("twice", 1, a => Twice(a[0])))
            .AddFunction(new CelFunction("pick", CelCallStyle.Global, 1, 3, call => call[0]));

        // Act.
        var refused = Record.Exception(() => environment.Compile(expression));

        // Assert.
        Assert.IsType<CelException>(refused);
        Assert.Equal(message, refused.Message);
    }

    [Fact]
    public void AVariadicFunctionTakesAnyNumberFromItsMinimum()
    {
        // Arrange.
        var environment = CelEnvironment.Standard()
            .AddFunction(new CelFunction("count", CelCallStyle.Global, 1, CelFunction.Variadic, call => (long)call.Count));

        // Act and assert.
        Assert.Equal(5L, Evaluate.Expression("count(1, 2, 3, 4, 5)", environment: environment));
        Assert.Equal("The function 'count' takes at least 1 argument, not 0, in 'count()'.", Record.Exception(() => environment.Compile("count()")).Message);
    }

    [Fact]
    public void AFunctionsCostIsChargedPerCall_AndTheBudgetEndsTheEvaluation()
    {
        // Arrange.
        var environment = CelEnvironment.Standard()
            .AddFunction(CelFunction.Global("cheap", 0, _ => 1L))
            .AddFunction(CelFunction.Global("dear", 0, _ => 1L, _ => 600));
        environment.Budget = 1_000;

        // Act.
        var cheap = environment.Compile("cheap() + cheap()").Evaluate(new Dictionary<string, object?>());
        var dear = environment.Compile("dear() + dear()").Evaluate(new Dictionary<string, object?>());

        // Assert.
        Assert.Equal(2L, cheap);
        Assert.Equal("error: The expression exceeded its evaluation budget.", Evaluate.Text(dear));
    }

    [Fact]
    public void AFunctionThatEvaluatesAnotherExpressionSharesTheBudget()
    {
        // Arrange: inner() evaluates a program on the caller's budget, so its steps count against it.
        var inner = CelEnvironment.Standard().Compile("[1, 2, 3, 4, 5].map(x, x * x).size()");
        var environment = CelEnvironment.Standard()
            .AddFunction(new CelFunction("inner", CelCallStyle.Global, 0, 0, call => inner.Evaluate(new Dictionary<string, object?>(), call.Budget)));
        var budget = new CelBudget(1_000);

        // Act.
        var value = environment.Compile("inner() + inner()").Evaluate(new Dictionary<string, object?>(), budget);

        // Assert.
        Assert.Equal(10L, value);
        Assert.True(budget.Used > 30, $"The nested evaluations charged only {budget.Used} steps.");
    }

    [Fact]
    public void ARegisteredMacroIsAComprehension()
    {
        // Arrange: count(x, p), as DISL §12.4 declares it.
        var environment = CelEnvironment.Standard()
            .AddMacro(new CelMacro("count", (items, body) => (long)items.Count(item => body(item) is true)));

        // Act.
        var value = Evaluate.Expression("[1, 2, 3, 4].count(n, n % 2 == 0)", environment: environment);

        // Assert.
        Assert.Equal(2L, value);
    }

    [Fact]
    public void AMacroTheEnvironmentLacksIsRefusedAsAFunction()
    {
        // Act.
        var refused = Record.Exception(() => new CelEnvironment().Compile("[1].all(x, x > 0)"));

        // Assert.
        Assert.Equal("The function 'all' is not supported by this CEL evaluator.", refused.Message);
    }

    [Fact]
    public void ACloneIsExtendedWithoutChangingItsOriginal()
    {
        // Arrange.
        var original = CelEnvironment.Standard().DeclareVariable("a");

        // Act.
        var clone = original.Clone().DeclareVariable("b").AddFunction(CelFunction.Global("twice", 1, a => Twice(a[0])));

        // Assert.
        Assert.Equal(["a"], original.Variables);
        Assert.Equal(["a", "b"], clone.Variables);
        Assert.False(original.TryGetFunction("twice", CelCallStyle.Global, out _));
        Assert.True(clone.TryGetFunction("twice", CelCallStyle.Global, out _));
    }

    [Fact]
    public void AVariableNamedLikeANamespaceIsSelectedFrom_NotCalledAsAQualifiedFunction()
    {
        // Arrange: 'math' is a variable here, so math.round(...) is a method call on it, which this environment lacks.
        var environment = CelEnvironment.Standard()
            .AddFunction(CelFunction.Global("math.half", 1, a => (double)a[0]! / 2))
            .DeclareVariable("math");

        // Act.
        var refused = Record.Exception(() => environment.Compile("math.half(3.0)"));
        var selected = environment.Compile("math.pi").Evaluate(new Dictionary<string, object?> { ["math"] = new CelMap { ["pi"] = 3.0 } });

        // Assert.
        Assert.Equal("The function 'half' is not supported by this CEL evaluator.", refused.Message);
        Assert.Equal(3.0, selected);
    }

    private static object Twice(object? value) => value switch
    {
        long l => l * 2,
        string s => s + s,
        _ => throw new CelException("twice() needs an int or a string."),
    };
}
