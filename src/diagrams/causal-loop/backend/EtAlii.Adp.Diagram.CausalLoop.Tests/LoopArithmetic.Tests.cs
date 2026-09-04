using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The reason this module exists (causal-loop-diagram Requirements 3.1, 3.2, 3.6): the loops the
/// arrows actually form, and whether each is reinforcing, derived rather than trusted.
/// </summary>
public class LoopArithmeticTests
{
    private static CausalLoopModel Model(params string[] statements) =>
        CausalLoopParser.Parse(CausalLoopDocument.Parse(string.Join("", statements.Select(s => s + "\r\n")))).Model;

    private static CausalLoopModel Ring(params string[] polarities)
    {
        // A ring of n variables, each linking to the next and the last back to the first, with
        // the given polarities in order. The smallest shape that makes exactly one loop.
        var names = Enumerable.Range(0, polarities.Length).Select(index => $"v{index}").ToArray();
        var statements = names.Select(name => $"variable {name}")
            .Concat(polarities.Select((polarity, index) =>
                $"link {names[index]} -> {names[(index + 1) % names.Length]} {polarity}".TrimEnd()))
            .ToArray();

        return Model(statements);
    }

    // ---- the cycles themselves -------------------------------------------------------------

    [Fact]
    public void AGraphWithNoCycle_FindsNone()
    {
        // Act.
        var result = CycleFinder.Find(Model("variable a", "variable b", "link a -> b +"));

        // Assert.
        Assert.Empty(result.Cycles);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ATwoVariableRing_IsOneCycle()
    {
        // Act.
        var cycle = Assert.Single(CycleFinder.Find(Ring("+", "+")).Cycles);

        // Assert.
        Assert.Equal(["v0", "v1"], cycle);
    }

    [Fact]
    public void ASelfLink_IsACycleOfOne()
    {
        // Act.
        // A variable that feeds itself is a feedback loop, and the shortest one there is.
        var cycle = Assert.Single(CycleFinder.Find(Model("variable a", "link a -> a +")).Cycles);

        // Assert.
        Assert.Equal(["a"], cycle);
    }

    [Fact]
    public void OverlappingCycles_AreFoundSeparately_AndEachOnlyOnce()
    {
        // Arrange.
        // Two loops sharing the a->b edge: a-b-c and a-b-d.
        var model = Model(
            "variable a", "variable b", "variable c", "variable d",
            "link a -> b +", "link b -> c +", "link c -> a +", "link b -> d +", "link d -> a +");

        // Act.
        var result = CycleFinder.Find(model);

        // Assert.
        Assert.Equal(2, result.Cycles.Count);
        Assert.All(result.Cycles, cycle => Assert.Equal(3, cycle.Count));
        // Each cycle appears once, not once per member - which is what restricting the subgraph
        // to vertices at or after the root buys.
        Assert.Equal(result.Cycles.Count, result.Cycles.Select(cycle => string.Join(",", cycle)).Distinct().Count());
    }

    [Fact]
    public void NestedCycles_AreBothFound()
    {
        // Arrange.
        // An outer ring a-b-c-d and an inner shortcut c->a making a second, shorter loop.
        var model = Model(
            "variable a", "variable b", "variable c", "variable d",
            "link a -> b +", "link b -> c +", "link c -> d +", "link d -> a +", "link c -> a +");

        // Act.
        var lengths = CycleFinder.Find(model).Cycles.Select(cycle => cycle.Count).Order().ToArray();

        // Assert.
        Assert.Equal([3, 4], lengths);
    }

    [Fact]
    public void ALinkToAnUndeclaredVariable_IsNotWalked()
    {
        // Act.
        // The validator reports the dangling link; the cycle search does not invent a node for it.
        var result = CycleFinder.Find(Model("variable a", "link a -> nowhere +", "link nowhere -> a +"));

        // Assert.
        Assert.Empty(result.Cycles);
    }

    [Fact]
    public void TheSearchIsStable_SoTheSameDocumentAlwaysReportsTheSameLoops()
    {
        // Arrange.
        var model = Model(
            "variable a", "variable b", "variable c",
            "link a -> b +", "link b -> c +", "link c -> a +", "link b -> a +");

        // Act.
        var first = CycleFinder.Find(model).Cycles.Select(cycle => string.Join(",", cycle)).ToArray();
        var second = CycleFinder.Find(model).Cycles.Select(cycle => string.Join(",", cycle)).ToArray();

        // Assert.
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheBound_StopsTheSearchAndSaysSo_RatherThanTruncatingSilently()
    {
        // Arrange.
        // A complete graph on five variables has far more than two elementary cycles.
        var names = Enumerable.Range(0, 5).Select(index => $"v{index}").ToArray();
        var model = Model(names.Select(name => $"variable {name}")
            .Concat(from a in names from b in names where a != b select $"link {a} -> {b} +")
            .ToArray());

        // Act.
        var bounded = CycleFinder.Find(model, bound: 2);

        // Assert.
        Assert.True(bounded.Truncated);
        Assert.Equal(2, bounded.Examined);
        // And unbounded it finds more, so the bound is what stopped it rather than the graph.
        Assert.True(CycleFinder.Find(model).Cycles.Count > 2);
    }

    // ---- the even/odd rule -----------------------------------------------------------------

    /// <summary>
    /// The rule, stated as a theory over the cases: reinforcing where the count of negative
    /// links is even, balancing where odd.
    /// </summary>
    [Theory]
    [InlineData(LoopPolarityResult.Reinforcing, "+", "+")]
    [InlineData(LoopPolarityResult.Balancing, "-", "+")]
    [InlineData(LoopPolarityResult.Balancing, "+", "-")]
    [InlineData(LoopPolarityResult.Reinforcing, "-", "-")]
    [InlineData(LoopPolarityResult.Reinforcing, "+", "+", "+")]
    [InlineData(LoopPolarityResult.Balancing, "-", "+", "+")]
    [InlineData(LoopPolarityResult.Reinforcing, "-", "-", "+")]
    [InlineData(LoopPolarityResult.Balancing, "-", "-", "-")]
    public void ParityDecidesTheLabel(LoopPolarityResult expected, params string[] polarities)
    {
        // Arrange.
        var model = Ring(polarities);
        var cycle = Assert.Single(CycleFinder.Find(model).Cycles);

        // Act & assert.
        Assert.Equal(expected, LoopPolarity.Of(model, cycle));
    }

    /// <summary>
    /// Zero is an even number, so a loop of nothing but positive links reinforces. This is the
    /// case a reader most often gets wrong, and the case every new document lands in.
    /// </summary>
    [Fact]
    public void ZeroNegativeLinks_IsEven_AndThereforeReinforcing()
    {
        // Arrange.
        var model = Ring("+", "+");
        var cycle = Assert.Single(CycleFinder.Find(model).Cycles);

        // Act & assert.
        Assert.Equal(0, LoopPolarity.NegativeCount(model, cycle));
        Assert.Equal(0, LoopPolarity.NegativeCount(model, cycle)!.Value % 2);
        Assert.Equal(LoopPolarityResult.Reinforcing, LoopPolarity.Of(model, cycle));
    }

    /// <summary>
    /// The distinction that makes Unstated a real case: an uncounted parity is not a parity of
    /// zero, and answering "reinforcing" here would be a confident claim about a diagram that
    /// states nothing (Requirement 3.6).
    /// </summary>
    [Fact]
    public void AnUnstatedLinkAnywhereInTheLoop_MakesItUndecidable_NotReinforcing()
    {
        // Arrange.
        var model = Ring("+", "");
        var cycle = Assert.Single(CycleFinder.Find(model).Cycles);

        // Act & assert.
        Assert.Equal(LoopPolarityResult.Undecidable, LoopPolarity.Of(model, cycle));
        Assert.NotEqual(LoopPolarityResult.Reinforcing, LoopPolarity.Of(model, cycle));
        Assert.Null(LoopPolarity.NegativeCount(model, cycle));
    }

    [Fact]
    public void ASelfLink_ObeysTheSameRule()
    {
        // Arrange & act & assert.
        var positive = Model("variable a", "link a -> a +");
        Assert.Equal(LoopPolarityResult.Reinforcing, LoopPolarity.Of(positive, ["a"]));

        var negative = Model("variable a", "link a -> a -");
        Assert.Equal(LoopPolarityResult.Balancing, LoopPolarity.Of(negative, ["a"]));
    }

    [Fact]
    public void ACycleNamingALinkTheDocumentDoesNotState_IsUndecidable()
    {
        // Arrange.
        // A loop statement can name a path the arrows do not actually form. That is a finding
        // rather than an answer, and the arithmetic must not fill the gap with a guess.
        var model = Model("variable a", "variable b", "link a -> b +");

        // Act & assert.
        Assert.Equal(LoopPolarityResult.Undecidable, LoopPolarity.Of(model, ["a", "b"]));
    }
}
