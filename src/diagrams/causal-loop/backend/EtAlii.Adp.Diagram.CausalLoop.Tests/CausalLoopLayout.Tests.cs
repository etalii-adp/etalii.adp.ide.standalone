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

    // ---- the ring follows the links, not the declaration order --------------------------------

    /// <summary>
    /// The defect this section was written for: a diagram opened as a tangle.
    /// </summary>
    /// <remarks>
    /// The ring placed variables in the order the document declared them, which says nothing
    /// about structure. A loop written as a, b, c, d but linked a-c-b-d ended up with its links
    /// crossing the middle of the ring instead of running around the rim, and the shape the
    /// notation exists to show was the one thing a reader could not see. Every layout test passed
    /// while that was true, because they asserted where boxes were and never how the links between
    /// them would run.
    /// </remarks>
    [Fact]
    public void TheRing_FollowsTheLinks_RatherThanTheOrderTheyWereDeclaredIn()
    {
        // Arrange.
        // Declared a, b, c, d; linked so the cycle runs a, c, b, d.
        var model = Model(
            "causal-loop 1",
            "variable a \"A\"", "variable b \"B\"", "variable c \"C\"", "variable d \"D\"",
            "link a -> c +", "link c -> b +", "link b -> d +", "link d -> a +");

        // Act.
        var order = CausalLoopLayout.RingOrder(model).Select(variable => variable.Id).ToArray();

        // Assert.
        Assert.Equal(["a", "c", "b", "d"], order);
    }

    /// <summary>
    /// The ordering reaches the drawn positions, not just the helper that computes it.
    /// </summary>
    /// <remarks>
    /// <b>Found by sabotage, and it is the whole point of this test.</b> The first version of
    /// these tests called <c>RingOrder</c> directly. Reverting <c>Compute</c> to declaration
    /// order - which is exactly the defect - passed every one of them, because nothing asserted
    /// that <c>Compute</c> used the order at all. The unit was right and the wiring was untested,
    /// which is the same gap that let the dead context menu and the flat arcs ship. So this reads
    /// the boxes.
    /// </remarks>
    [Fact]
    public void TheComputedPositions_FollowTheRingOrder_AndNotTheDeclarationOrder()
    {
        // Arrange.
        // Declared a, b, c, d; linked so the cycle runs a, c, b, d.
        var model = Model(
            "causal-loop 1",
            "variable a \"A\"", "variable b \"B\"", "variable c \"C\"", "variable d \"D\"",
            "link a -> c +", "link c -> b +", "link b -> d +", "link d -> a +");

        // Act.
        var boxes = CausalLoopLayout.Compute(model);

        // Assert.
        Assert.Equal(4, boxes.Count);

        // Recover the seating from the drawing itself: the angle of each box about the ring's
        // centre, clockwise from the top, which is how Compute lays them out.
        var seated = boxes
            .OrderBy(entry => Math.Atan2(entry.Value.CenterY, entry.Value.CenterX))
            .Select(entry => entry.Key)
            .ToArray();

        // Rotation-independent: the ring has no first seat, only an order.
        var start = Array.IndexOf(seated, "a");
        var order = Enumerable.Range(0, seated.Length).Select(offset => seated[(start + offset) % seated.Length]).ToArray();

        Assert.Equal(["a", "c", "b", "d"], order);
    }

    /// <summary>
    /// A link is read undirected, so a variable everything points at still seats beside them.
    /// </summary>
    /// <remarks>
    /// Declared a, b, c and linked only <c>c -> a</c> and <c>c -> b</c>. Read undirected, the walk
    /// leaves a for c and then reaches b, giving a, c, b. Read directionally, a and b are dead
    /// ends and c is never reached from either, so the walk falls back to declaration order and
    /// gives a, b, c - the shape the ordering exists to avoid.
    /// </remarks>
    [Fact]
    public void ALink_SeatsItsEndsTogether_WhicheverWayItPoints()
    {
        // Arrange.
        var model = Model(
            "causal-loop 1",
            "variable a \"A\"", "variable b \"B\"", "variable c \"C\"",
            "link c -> a +", "link c -> b +");

        // Act.
        var order = CausalLoopLayout.RingOrder(model).Select(variable => variable.Id).ToArray();

        // Assert.
        Assert.Equal(["a", "c", "b"], order);
    }

    /// <summary>
    /// Where a variable has several neighbours, the earliest-declared is taken first - so the
    /// ring is a function of the document rather than of the order the links happen to be written.
    /// </summary>
    [Fact]
    public void ABranch_TakesItsEarliestDeclaredNeighbourFirst()
    {
        // Arrange.
        // The links name c before b, so an unsorted walk would seat c first.
        var model = Model(
            "causal-loop 1",
            "variable a \"A\"", "variable b \"B\"", "variable c \"C\"",
            "link a -> c +", "link a -> b +");

        // Act.
        var order = CausalLoopLayout.RingOrder(model).Select(variable => variable.Id).ToArray();

        // Assert.
        Assert.Equal(["a", "b", "c"], order);
    }

    /// <summary>
    /// What the ordering is actually for, measured on the arrangement rather than on the order:
    /// consecutive ring positions should be linked, so the links run around the rim.
    /// </summary>
    [Fact]
    public void MostLinks_JoinNeighboursOnTheRing()
    {
        // Arrange.
        // The on-call example's shape: three loops sharing variables, declared in reading order
        // rather than in link order - which is how anybody actually writes one.
        var model = Model(
            "causal-loop 1",
            "variable incidents \"Incidents\"", "variable onCallLoad \"Load\"",
            "variable fatigue \"Fatigue\"", "variable mistakes \"Mistakes\"",
            "variable attrition \"Attrition\"", "variable teamSize \"Team\"",
            "link incidents -> onCallLoad +", "link onCallLoad -> fatigue +",
            "link fatigue -> mistakes +", "link mistakes -> incidents +",
            "link fatigue -> attrition +", "link attrition -> teamSize -",
            "link teamSize -> onCallLoad -");

        // Act.
        var order = CausalLoopLayout.RingOrder(model).Select(variable => variable.Id).ToArray();
        var seat = order.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);

        // How many ring seats apart the two ends of each link sit, the short way round.
        int Apart(CausalLoopLink link)
        {
            var gap = Math.Abs(seat[link.From] - seat[link.To]);
            return Math.Min(gap, order.Length - gap);
        }

        // Assert.
        Assert.NotEmpty(model.Links);

        var adjacent = model.Links.Count(link => Apart(link) == 1);
        Assert.True(
            adjacent * 2 >= model.Links.Count,
            $"Only {adjacent} of {model.Links.Count} links join ring neighbours; the rest cut across.");
    }

    /// <summary>
    /// Deterministic, like everything else that decides a position: the same document gives the
    /// same ring, and the ring is a function of the document rather than of the walk's own order.
    /// </summary>
    [Fact]
    public void TheRingOrder_IsAFunctionOfTheDocument()
    {
        // Arrange.
        var text = new[]
        {
            "causal-loop 1",
            "variable a \"A\"", "variable b \"B\"", "variable c \"C\"",
            "link a -> b +", "link b -> c +", "link c -> a +",
        };

        // Act.
        var first = CausalLoopLayout.RingOrder(Model(text)).Select(variable => variable.Id);
        var second = CausalLoopLayout.RingOrder(Model(text)).Select(variable => variable.Id);

        // Assert.
        Assert.Equal(first, second);
    }

    [Fact]
    public void EveryVariable_TakesASeat_EvenWithNoLinksAtAll()
    {
        // Arrange.
        // Nothing to follow, so the walk falls back to declaration order - and places all of them.
        var model = Model(
            "causal-loop 1",
            "variable a \"A\"", "variable b \"B\"", "variable c \"C\"");

        // Act.
        var order = CausalLoopLayout.RingOrder(model).Select(variable => variable.Id).ToArray();

        // Assert.
        Assert.Equal(["a", "b", "c"], order);
    }

    /// <summary>
    /// Two disconnected loops do not interleave: each is walked out before the other begins, so
    /// each keeps its own arc of the rim.
    /// </summary>
    [Fact]
    public void DisconnectedComponents_KeepTheirOwnStretchOfTheRing()
    {
        // Arrange.
        var model = Model(
            "causal-loop 1",
            "variable a \"A\"", "variable x \"X\"", "variable b \"B\"", "variable y \"Y\"",
            "link a -> b +", "link b -> a +",
            "link x -> y +", "link y -> x +");

        // Act.
        var order = CausalLoopLayout.RingOrder(model).Select(variable => variable.Id).ToArray();

        // Assert.
        // a and b together, x and y together - not a, x, b, y.
        Assert.Equal(["a", "b", "x", "y"], order);
    }

}
