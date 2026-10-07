using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>User functions (DISL §3.4): compiled in declaration order, recursion only when bounded, context variables only through <c>uses</c>.</summary>
public class UserFunctionsTests
{
    private static object? Evaluate(DislSpecification specification, string expression, IReadOnlyDictionary<string, object?>? variables = null) =>
        specification.Environment(DislContexts.Element).Compile(expression).Evaluate(variables ?? new Dictionary<string, object?>());

    [Fact]
    public void AFunction_CallsTheFunctionsDeclaredBeforeIt()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "functions": {
              "twice": { "params": [ { "name": "n", "type": "int" } ], "returns": "int", "cel": "n * 2" },
              "quadruple": { "params": [ { "name": "n", "type": "int" } ], "returns": "int", "cel": "twice(twice(n))" } }
            """));

        Assert.Equal(20L, Evaluate(specification, "quadruple(5)"));
        Assert.Equal(["twice", "quadruple"], specification.Functions.Select(function => function.Name));
    }

    [Fact]
    public void AFunctionCallingOneDeclaredAfterIt_IsRefused()
    {
        var json = Specifications.With("""
            "functions": {
              "quadruple": { "params": [ { "name": "n", "type": "int" } ], "returns": "int", "cel": "twice(twice(n))" },
              "twice": { "params": [ { "name": "n", "type": "int" } ], "returns": "int", "cel": "n * 2" } }
            """);

        Assert.Equal(["error at /functions/quadruple/cel: 'quadruple' calls 'twice', which is declared after it or does not compile; a function may call only the functions declared before it (DISL §3.4)."], Specifications.Diagnostics(json));
    }

    [Fact]
    public void AFunctionCallingItselfWithoutRecursion_IsRefused()
    {
        var json = Specifications.With("""
            "functions": { "loop": { "params": [ { "name": "n", "type": "int" } ], "returns": "int", "cel": "n == 0 ? 0 : loop(n - 1)" } }
            """);

        Assert.Equal(["error at /functions/loop/cel: 'loop' calls itself, which a function may do only when it declares recursion (DISL §3.4)."], Specifications.Diagnostics(json));
    }

    /// <summary>
    /// sum(n) = n + sum(n - 1), four calls deep at most: sum(3) reaches sum(0) at depth 4 and is exact;
    /// sum(4) would reach sum(0) at depth 5, and sum(5) sum(1), which give atMaxDepth over their own n.
    /// </summary>
    [Theory]
    [InlineData(1, 1L)]
    [InlineData(3, 6L)]
    [InlineData(4, 4L + 3 + 2 + 1 - 1000)]
    [InlineData(5, 5L + 4 + 3 + 2 - 1001)]
    public void ABoundedRecursiveFunction_GivesAtMaxDepthBeyondItsDepth(int n, long expected)
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "functions": { "sum": { "params": [ { "name": "n", "type": "int" } ], "returns": "int",
              "recursion": { "maxDepth": 4, "atMaxDepth": "-1000 - n" },
              "cel": "n == 0 ? 0 : n + sum(n - 1)" } }
            """));

        Assert.Equal(expected, Evaluate(specification, $"sum({n})"));
        Assert.Equal(expected, Evaluate(specification, $"sum({n})"));
    }

    [Fact]
    public void AFunction_ReadsDiagramOnlyThroughUses()
    {
        var refused = Specifications.With("""
            "functions": { "count": { "params": [], "returns": "int", "cel": "size(diagram)" } }
            """);
        var allowed = Specifications.Loaded(Specifications.With("""
            "functions": { "count": { "params": [], "returns": "int", "uses": ["diagram"], "cel": "size(diagram)" } }
            """));

        Assert.StartsWith("error at /functions/count/cel: 'diagram' is not a variable here", Assert.Single(Specifications.Diagnostics(refused)), StringComparison.Ordinal);
        Assert.Equal(2L, Evaluate(allowed, "count()", new Dictionary<string, object?> { ["diagram"] = new CelMap { ["a"] = 1L, ["b"] = 2L }, ["self"] = null, ["env"] = null }));
    }

    [Fact]
    public void AFunctionDoesNotSeeTheVariablesOfItsCaller()
    {
        var json = Specifications.With("""
            "functions": { "name": { "params": [], "returns": "string", "cel": "self.name" } }
            """);

        Assert.StartsWith("error at /functions/name/cel: 'self' is not a variable here", Assert.Single(Specifications.Diagnostics(json)), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingFunction_FailsTheExpressionThatCallsIt()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "functions": { "half": { "params": [ { "name": "n", "type": "int" } ], "returns": "int", "cel": "10 / n" } }
            """));

        Assert.Equal(new CelError("Division by zero."), Evaluate(specification, "half(0) + 1"));
    }
}
