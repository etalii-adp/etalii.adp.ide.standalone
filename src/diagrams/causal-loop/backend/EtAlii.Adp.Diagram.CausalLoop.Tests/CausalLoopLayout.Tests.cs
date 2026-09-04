using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The computed layout (causal-loop-diagram Requirements 7.1, 7.2), and the two inherited
/// obligations it meets by construction rather than by effort: no overlap, and an extent whose
/// dimensions stay within four times each other.
/// </summary>
public class CausalLoopLayoutTests
{
    private static CausalLoopModel Model(params string[] statements) =>
        CausalLoopParser.Parse(CausalLoopDocument.Parse(string.Join("", statements.Select(s => s + "\r\n")))).Model;

    private static CausalLoopModel Ring(int count) =>
        Model([.. Enumerable.Range(0, count).Select(index => $"variable v{index} \"Variable {index}\"")]);

    [Fact]
    public void EveryDeclaredVariable_IsPlaced()
    {
        // Arrange.
        var model = Ring(7);

        // Act.
        var boxes = CausalLoopLayout.Compute(model);

        // Assert.
        // The floor first: an empty result satisfies the claim below vacuously.
        Assert.NotEmpty(boxes);
        Assert.Equal(model.Variables.Count, boxes.Count);
        Assert.All(model.Variables, variable => Assert.True(boxes.ContainsKey(variable.Id)));
    }

    /// <summary>
    /// Requirement 7.1's exact case. One variable is legitimately placed at the origin, and that
    /// has to stay tellable from a variable the layout never placed - which is why the layout
    /// returns a dictionary and callers ask rather than defaulting.
    /// </summary>
    [Fact]
    public void AVariableGenuinelyAtTheOrigin_IsDistinguishableFromAnUnplacedOne()
    {
        // Arrange.
        var boxes = CausalLoopLayout.Compute(Model("variable only \"Only\""));

        // Act & assert.
        Assert.True(boxes.TryGetValue("only", out var placed));
        Assert.Equal(0, placed.X);
        Assert.Equal(0, placed.Y);

        // And the one that was never placed answers differently rather than answering (0, 0).
        Assert.False(boxes.TryGetValue("absent", out _));
    }

    [Fact]
    public void AnEmptyDocument_PlacesNothing_RatherThanPlacingSomethingAtTheOrigin()
    {
        // Act & assert.
        Assert.Empty(CausalLoopLayout.Compute(CausalLoopModel.Empty));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(25)]
    [InlineData(60)]
    public void NoTwoVariablesOverlap_AtAnySize(int count)
    {
        // Arrange.
        var boxes = CausalLoopLayout.Compute(Ring(count)).Values.ToArray();

        // Act & assert.
        Assert.Equal(count, boxes.Length);
        for (var left = 0; left < boxes.Length; left++)
        {
            for (var right = left + 1; right < boxes.Length; right++)
            {
                Assert.False(
                    boxes[left].Overlaps(boxes[right]),
                    $"boxes {left} and {right} overlap at {count} variables");
            }
        }
    }

    [Fact]
    public void ALongLabelWidensItsOwnBox_AndTheRingGrowsToKeepThemApart()
    {
        // Arrange.
        var model = Model(
            "variable a \"a\"",
            "variable b \"an extremely long variable name that will not fit in the minimum width\"",
            "variable c \"c\"");

        // Act.
        var boxes = CausalLoopLayout.Compute(model);

        // Assert.
        Assert.True(boxes["b"].Width > boxes["a"].Width);
        for (var left = 0; left < 3; left++)
        {
            foreach (var other in new[] { "a", "b", "c" }.Where(id => id != new[] { "a", "b", "c" }[left]))
            {
                Assert.False(boxes[new[] { "a", "b", "c" }[left]].Overlaps(boxes[other]));
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(40)]
    public void TheExtentStaysInsideTheRatioBound(int count)
    {
        // Arrange.
        var boxes = CausalLoopLayout.Compute(Ring(count)).Values.ToArray();

        // Act.
        var width = boxes.Max(box => box.Right) - boxes.Min(box => box.X);
        var height = boxes.Max(box => box.Bottom) - boxes.Min(box => box.Y);

        // Assert.
        // A ring is square, so this is met with room to spare - which is the point of choosing
        // one rather than a layering that would have to be balanced afterwards.
        Assert.True(Math.Max(width, height) <= 4 * Math.Min(width, height), $"{width} by {height} at {count} variables");
    }

    [Fact]
    public void TheLayoutIsDeterministic()
    {
        // Arrange.
        var model = Ring(12);

        // Act.
        var first = CausalLoopLayout.Compute(model);
        var second = CausalLoopLayout.Compute(model);

        // Assert.
        Assert.Equal(first.OrderBy(entry => entry.Key, StringComparer.Ordinal), second.OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void TheRingRunsClockwiseFromTheTop_SoAReaderTracesItTheWayTheyRead()
    {
        // Arrange.
        var boxes = CausalLoopLayout.Compute(Ring(4));

        // Act & assert.
        // First at the top, second to the right, third at the bottom, fourth to the left.
        Assert.True(boxes["v0"].CenterY < boxes["v2"].CenterY);
        Assert.True(boxes["v1"].CenterX > boxes["v3"].CenterX);
        Assert.True(Math.Abs(boxes["v0"].CenterX) < 0.001);
        Assert.True(Math.Abs(boxes["v1"].CenterY) < 0.001);
    }

    [Fact]
    public void AVariableWithNoLabel_IsSizedFromItsId()
    {
        // Act.
        var boxes = CausalLoopLayout.Compute(Model("variable population", "variable b"));

        // Assert.
        Assert.True(boxes["population"].Width > boxes["b"].Width);
    }
}
