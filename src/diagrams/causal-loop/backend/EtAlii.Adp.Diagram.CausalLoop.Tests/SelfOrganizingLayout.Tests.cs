using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The self-organizing layout (causal-loop-diagram Requirement 6): Meyer's method with every
/// source of randomness replaced by a function of the document and the iteration index.
/// </summary>
public class SelfOrganizingLayoutTests
{
    /// <summary>
    /// Set on the child process of the two-process determinism test; names the file it writes its
    /// positions to. Absent in an ordinary run, which is what skips the emitter.
    /// </summary>
    internal const string EmitToVariable = "ETALII_ADP_CAUSAL_LOOP_LAYOUT_OUT";

    /// <summary>The line ending this module's documents use, named so a fixture never spells it.</summary>
    private const string Newline = "\r\n";

    private const string EmitterFilter =
        "/*/*/SelfOrganizingLayoutTests/EmitsPositionsForTheParentProcess";

    /// <summary>
    /// Two feedback loops sharing a variable, plus an unconnected pair. Small enough to reason
    /// about and structured enough that graph distance says something different from layout
    /// distance, which is the property the method turns on.
    /// </summary>
    private static CausalLoopModel Fixture() => Parse(
        "causal-loop 1\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "variable crowding \"Crowding\"\r\n"
        + "variable deaths \"Deaths\"\r\n"
        + "variable morale \"Morale\"\r\n"
        + "variable rumour \"Rumour\"\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "link population -> crowding +\r\n"
        + "link crowding -> deaths +\r\n"
        + "link deaths -> population -\r\n"
        + "link morale -> rumour +\r\n"
        + "link rumour -> morale +\r\n");

    private static CausalLoopModel Parse(string text) =>
        CausalLoopParser.Parse(CausalLoopDocument.Parse(text)).Model;

    /// <summary>A chain of <paramref name="count"/> variables closing into one long loop.</summary>
    private static CausalLoopModel Ring(int count)
    {
        var text = new StringBuilder("causal-loop 1\r\n");
        for (var index = 0; index < count; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"variable v{index} \"Variable {index}\"\r\n");
        }

        for (var index = 0; index < count; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"link v{index} -> v{(index + 1) % count} +\r\n");
        }

        return Parse(text.ToString());
    }

    // ---- determinism, in two processes --------------------------------------------------------

    /// <summary>
    /// Requirement 6.3, and the reason it is worth the machinery. A same-process comparison runs
    /// the same loaded code against the same warmed state and would pass against an implementation
    /// that cached its answer, seeded itself once from a clock, or keyed anything off a hash code.
    /// Only a second process starts from nothing.
    /// </summary>
    [Fact]
    public void TheSameDocument_LaidOutInTwoProcesses_GivesIdenticalPositions()
    {
        // Arrange.
        var expected = Serialize(SelfOrganizingLayout.Compute(Fixture()));
        var output = IoPath.Combine(IoPath.GetTempPath(), $"adp-cld-layout-{Guid.NewGuid():N}.txt");

        // Act.
        var host = Environment.ProcessPath;
        Assert.SkipWhen(host is null, "The test host's own path is unknown, so no child can be started.");

        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-filter");
        start.ArgumentList.Add(EmitterFilter);
        start.Environment[EmitToVariable] = output;

        using var child = Process.Start(start);
        Assert.NotNull(child);

        var log = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
        Assert.True(child.WaitForExit(120_000), "The child process did not finish within two minutes.");

        try
        {
            // Assert.
            Assert.True(File.Exists(output), $"The child wrote no positions. Its output was:\r\n{log}");

            // Byte for byte, on round-trippable doubles: "close enough" would let a layout that
            // reads a clock or a hash code pass, since those differ by a little and not by a lot.
            Assert.Equal(expected, File.ReadAllText(output));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    /// <summary>
    /// The other half of the test above: this is what the child process runs. It is skipped in an
    /// ordinary run, where nothing has asked it for anything.
    /// </summary>
    [Fact]
    public void EmitsPositionsForTheParentProcess()
    {
        // Arrange.
        var output = Environment.GetEnvironmentVariable(EmitToVariable);
        Assert.SkipWhen(
            string.IsNullOrEmpty(output),
            "Only the two-process determinism test asks for this; nothing did.");

        // Act & assert.
        File.WriteAllText(output, Serialize(SelfOrganizingLayout.Compute(Fixture())));
    }

    /// <summary>Positions as text, round-trippable so the comparison is exact rather than near.</summary>
    private static string Serialize(SelfOrganizingResult result)
    {
        var text = new StringBuilder();
        foreach (var (id, box) in result.Boxes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"{id} {box.X:R} {box.Y:R} {box.Width:R} {box.Height:R}\n");
        }

        return text.ToString();
    }

    // ---- a pure function of the document ------------------------------------------------------

    [Fact]
    public void TheLayout_IsAFunctionOfTheDocument_AndNotOfHowItWasBuilt()
    {
        // Act.
        // The same text parsed twice into two separate models, which share no object at all.
        var first = SelfOrganizingLayout.Compute(Fixture());
        var second = SelfOrganizingLayout.Compute(Fixture());

        // Assert.
        Assert.Equal(Serialize(first), Serialize(second));
    }

    /// <summary>
    /// The stimulus sequence is the second source of randomness, and this is what it was replaced
    /// with. A radical-inverse sequence has published values, so these are checked against the
    /// definition rather than against whatever the implementation happens to return.
    /// </summary>
    [Theory]
    [InlineData(1, 2, 0.5)]
    [InlineData(2, 2, 0.25)]
    [InlineData(3, 2, 0.75)]
    [InlineData(4, 2, 0.125)]
    [InlineData(1, 3, 1.0 / 3)]
    [InlineData(2, 3, 2.0 / 3)]
    [InlineData(3, 3, 1.0 / 9)]
    public void TheStimulusSequence_IsTheRadicalInverseOfItsIndex(int index, int radix, double expected)
    {
        // Act & assert.
        Assert.Equal(expected, SelfOrganizingLayout.Halton(index, radix), 12);
    }

    /// <summary>
    /// Low-discrepancy is not a synonym for random, and the difference is the point: the sequence
    /// covers the interval more evenly than random samples would, which is what the method wanted
    /// from randomness in the first place.
    /// </summary>
    [Fact]
    public void TheStimulusSequence_CoversTheIntervalEvenly()
    {
        // Arrange.
        var buckets = new int[10];

        // Act.
        for (var index = 1; index <= 1000; index++)
        {
            buckets[(int)(SelfOrganizingLayout.Halton(index, 2) * 10)]++;
        }

        // Assert.
        // A thousand samples in ten buckets: a uniform random draw would routinely stray further
        // from a hundred than this sequence ever does.
        Assert.All(buckets, count => Assert.InRange(count, 95, 105));
    }

    /// <summary>
    /// The first source of randomness. A spiral in document order starts nothing on top of
    /// anything else without consulting a random source.
    /// </summary>
    [Fact]
    public void TheInitialPositions_AreASpreadAndNotAPile()
    {
        // Arrange.
        var widths = Enumerable.Repeat(120.0, 40).ToArray();

        // Act.
        var positions = SelfOrganizingLayout.InitialPositions(40, widths, 30, 40);

        // Assert.
        Assert.Equal((0, 0), positions[0]);

        var distinct = positions
            .Select(position => (Math.Round(position.X, 6), Math.Round(position.Y, 6)))
            .Distinct()
            .Count();
        Assert.Equal(positions.Length, distinct);
    }

    // ---- graph distance, which is what makes it a graph layout ---------------------------------

    /// <summary>
    /// The neighbourhood is measured in hops, not in pixels. That is the difference between a
    /// graph layout and a clustering of points, and it is worth a test of its own because the
    /// method still runs — badly — if it is measured wrong.
    /// </summary>
    [Fact]
    public void GraphDistance_IsCountedInHopsOverUndirectedLinks()
    {
        // Arrange.
        var model = Fixture();

        // Act.
        var hops = SelfOrganizingLayout.GraphDistances(model, model.Variables);
        var index = model.Variables.Select((variable, position) => (variable.Id, position))
            .ToDictionary(entry => entry.Id, entry => entry.position, StringComparer.Ordinal);

        // Assert.
        Assert.Equal(0, hops[index["population"]][index["population"]]);
        Assert.Equal(1, hops[index["population"]][index["births"]]);
        // Two hops: crowding reaches births only through population.
        Assert.Equal(2, hops[index["crowding"]][index["births"]]);

        // Read undirected: deaths -> population is the only link between them, and each is one
        // hop from the other. A directional reading would put population at infinity from deaths.
        Assert.Equal(1, hops[index["deaths"]][index["population"]]);
        Assert.Equal(1, hops[index["population"]][index["deaths"]]);

        // Unreachable is not far away, and is marked as its own thing.
        Assert.Equal(-1, hops[index["population"]][index["morale"]]);
    }

    /// <summary>
    /// A variable in another component has no causal relationship to the one being pulled, so it
    /// is not dragged toward it. Two disconnected loops arrange separately.
    /// </summary>
    [Fact]
    public void ADisconnectedComponent_IsNotDraggedTowardTheOtherOne()
    {
        // Arrange.
        var model = Fixture();

        // Act.
        var boxes = SelfOrganizingLayout.Compute(model).Boxes;

        // Assert.
        // Every one of the five connected variables ends up closer to its own component's members
        // than the two components' centres are to each other would be a weaker claim; the honest
        // one is that the pair moved as a pair.
        var morale = boxes["morale"];
        var rumour = boxes["rumour"];
        var population = boxes["population"];

        var withinPair = Distance(morale, rumour);
        var acrossComponents = Math.Min(Distance(morale, population), Distance(rumour, population));
        Assert.True(
            withinPair < acrossComponents,
            $"The unconnected pair ({withinPair:F0} apart) should stay closer to each other than to the other component ({acrossComponents:F0}).");
    }

    /// <summary>
    /// What the method is for: variables the document links end up near each other, and the
    /// arrangement reflects the causal structure rather than the order they were declared in.
    /// </summary>
    [Fact]
    public void LinkedVariables_EndUpNearerThanUnlinkedOnes()
    {
        // Arrange.
        var model = Ring(12);

        // Act.
        var boxes = SelfOrganizingLayout.Compute(model).Boxes;

        // Assert.
        // v0's neighbours around the ring are v1 and v11; its opposite is v6.
        var neighbour = Distance(boxes["v0"], boxes["v1"]);
        var opposite = Distance(boxes["v0"], boxes["v6"]);
        Assert.True(
            neighbour < opposite,
            $"A linked neighbour ({neighbour:F0}) should end up nearer than the variable opposite it ({opposite:F0}).");
    }

    private static double Distance(CausalLoopBox first, CausalLoopBox second)
    {
        var dx = first.CenterX - second.CenterX;
        var dy = first.CenterY - second.CenterY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // ---- the separation pass, and what it refuses ----------------------------------------------

    /// <summary>
    /// Requirement 6.5, the ground the ruling says decides whether this layout is real. A
    /// converged self-organizing map does not guarantee non-overlap, so the arrangement is
    /// separated rather than trusted, and this is measured on the arrangement the layout actually
    /// produces rather than on a hand-placed fixture.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(40)]
    [InlineData(120)]
    public void AtEverySize_NoTwoBoxesIntersect(int count)
    {
        // Act.
        var result = SelfOrganizingLayout.Compute(Ring(count));

        // Assert.
        Assert.True(result.IsArranged, result.Refusal);

        // Requirement 7.4: the claim below is vacuously true of a layout that placed nothing.
        var boxes = result.Boxes.Values.ToArray();
        Assert.Equal(count, boxes.Length);

        foreach (var (first, second) in Pairs(boxes))
        {
            Assert.False(
                first.Overlaps(second),
                $"Two of {count} variables overlap: {first} and {second}.");
        }
    }

    /// <summary>
    /// The one the ruling names, spelled out because it is the trap. A hairball is roughly square:
    /// it scores near 1:1 and sails past an extent-ratio bound while being exactly the unreadable
    /// arrangement the rule exists to stop. So the ratio is deliberately NOT the guard — this test
    /// shows a dense diagram passing the ratio it would have been judged by, and being judged on
    /// intersection instead.
    /// </summary>
    [Fact]
    public void TheExtentRatio_IsNotWhatGuardsReadability()
    {
        // Arrange.
        // A dense graph: every variable linked to every other, which is the shape that piles up.
        var text = new StringBuilder("causal-loop 1\r\n");
        const int count = 24;
        for (var index = 0; index < count; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"variable v{index} \"Variable {index}\"\r\n");
        }

        for (var first = 0; first < count; first++)
        {
            for (var second = first + 1; second < count; second++)
            {
                text.Append(CultureInfo.InvariantCulture, $"link v{first} -> v{second} +\r\n");
            }
        }

        // Act.
        var result = SelfOrganizingLayout.Compute(Parse(text.ToString()));
        Assert.True(result.IsArranged, result.Refusal);
        var boxes = result.Boxes.Values.ToArray();

        // Assert.
        // The ratio this arrangement would have been judged by, had the ratio been the guard.
        var width = boxes.Max(box => box.Right) - boxes.Min(box => box.X);
        var height = boxes.Max(box => box.Bottom) - boxes.Min(box => box.Y);
        var ratio = Math.Max(width, height) / Math.Min(width, height);
        Assert.InRange(ratio, 1.0, 3.0);

        // And the guard that actually decides: no two boxes share any area.
        Assert.NotEmpty(boxes);
        foreach (var (first, second) in Pairs(boxes))
        {
            Assert.False(first.Overlaps(second), $"A near-square arrangement still overlaps: {first} and {second}.");
        }
    }

    /// <summary>
    /// Requirement 6.7. The refusal is the correct outcome and not a failure of nerve: a diagram
    /// delivered as a hairball is worse than a diagram left alone. It is reachable, it names the
    /// size, and it hands back nothing to draw.
    /// </summary>
    [Fact]
    public void WhenTheBoxesCannotBeSeparated_TheLayoutRefusesNamingTheSize()
    {
        // Arrange.
        // Forty boxes started on top of one another, with one round to move them: the rounds run
        // out before the overlaps do, which is precisely the condition the requirement is about.
        var positions = new (double X, double Y)[40];
        var widths = Enumerable.Repeat(120.0, 40).ToArray();

        // Act.
        var remaining = SelfOrganizingLayout.Separate(positions, widths, 30, 40);
        var refusal = SelfOrganizingResult.CouldNotSeparate(40, remaining);

        // Assert.
        Assert.False(refusal.IsArranged);
        Assert.Equal(40, refusal.Size);
        Assert.Contains("40 variables", refusal.Refusal, StringComparison.Ordinal);
        Assert.Empty(refusal.Boxes);
    }

    /// <summary>
    /// The clearance a pair needs is measured from the boxes, not from the separation constant.
    /// </summary>
    /// <remarks>
    /// Found by sabotage, and worth the extra test. Dropping the box height from the vertical
    /// clearance - asking only for the separation between two centres - passed every other test
    /// in this class, because the default metrics happen to make the separation (40) slightly
    /// larger than the box height (39.6), so the wrong arithmetic still cleared the boxes. It is
    /// only a defect when a diagram is drawn with taller text, which is exactly the case no
    /// default-metrics fixture reaches. So this one sets the metrics that expose it.
    /// </remarks>
    [Fact]
    public void TheClearance_IsMeasuredFromTheBoxes_AndNotFromTheSeparationAlone()
    {
        // Arrange.
        // Tall text and a narrow gap: a pair separated by the gap alone would still overlap by
        // most of their height.
        var metrics = new CausalLoopMetrics(FontSize: 100, Separation: 10);
        Assert.True(
            metrics.Height > metrics.Separation,
            "This test is only meaningful while the box is taller than the separation.");

        // Act.
        var result = SelfOrganizingLayout.Compute(Ring(16), metrics);

        // Assert.
        Assert.True(result.IsArranged, result.Refusal);

        var boxes = result.Boxes.Values.ToArray();
        Assert.Equal(16, boxes.Length);
        foreach (var (first, second) in Pairs(boxes))
        {
            Assert.False(first.Overlaps(second), $"Tall boxes overlap: {first} and {second}.");
        }
    }

    /// <summary>
    /// The refusal is reachable through <c>Compute</c> itself, not only through the pieces. A
    /// refusal nothing can trigger is a refusal nobody has checked, and it would sit in the code
    /// looking like compliance while never once having run.
    /// </summary>
    [Fact]
    public void TheRefusal_IsReachableThroughTheLayoutItself()
    {
        // Arrange.
        // A dense graph the self-organizing pass packs tightly, given one round to separate it.
        var text = new StringBuilder("causal-loop 1" + Newline);
        const int count = 30;
        for (var index = 0; index < count; index++)
        {
            text.Append(CultureInfo.InvariantCulture, $"variable v{index} \"Variable {index}\"{Newline}");
        }

        for (var first = 0; first < count; first++)
        {
            for (var second = first + 1; second < count; second++)
            {
                text.Append(CultureInfo.InvariantCulture, $"link v{first} -> v{second} +{Newline}");
            }
        }

        // Act.
        var result = SelfOrganizingLayout.Compute(Parse(text.ToString()), rounds: 1);

        // Assert.
        Assert.False(result.IsArranged);
        Assert.Equal(count, result.Size);
        Assert.Contains("30 variables", result.Refusal, StringComparison.Ordinal);

        // Nothing to draw: the diagram is left as it was rather than delivered as a hairball.
        Assert.Empty(result.Boxes);

        // And the same document with the real number of rounds is arranged, so the refusal is
        // about the rounds running out and not about the document being impossible.
        Assert.True(SelfOrganizingLayout.Compute(Parse(text.ToString())).IsArranged);
    }

    /// <summary>
    /// The rounds are bounded rather than "until no overlaps remain". An unbounded loop on a
    /// document it cannot satisfy would spin instead of refusing, and Requirement 6.7 wants the
    /// refusal.
    /// </summary>
    [Fact]
    public void TheSeparationPass_Terminates_EvenWhenItCannotSucceed()
    {
        // Arrange.
        // Every box at the same point and far wider than the rounds can clear.
        var positions = new (double X, double Y)[200];
        var widths = Enumerable.Repeat(4000.0, 200).ToArray();

        // Act.
        var remaining = SelfOrganizingLayout.Separate(positions, widths, 30, 40);

        // Assert.
        // It came back at all, which is the claim, and it came back honest about what is left.
        Assert.True(remaining >= 0);
    }

    /// <summary>
    /// The separation pass is as deterministic as the pass before it: pairs visited in document
    /// order, each round reading what the last left, displacement a function of the overlap alone.
    /// </summary>
    [Fact]
    public void TheSeparationPass_MovesBoxesTheSameWayEveryTime()
    {
        // Arrange.
        var widths = Enumerable.Repeat(120.0, 30).ToArray();
        var first = new (double X, double Y)[30];
        var second = new (double X, double Y)[30];
        for (var index = 0; index < 30; index++)
        {
            first[index] = (index % 3, index % 5);
            second[index] = (index % 3, index % 5);
        }

        // Act.
        SelfOrganizingLayout.Separate(first, widths, 30, 40);
        SelfOrganizingLayout.Separate(second, widths, 30, 40);

        // Assert.
        Assert.Equal(first, second);
    }

    private static IEnumerable<(CausalLoopBox First, CausalLoopBox Second)> Pairs(CausalLoopBox[] boxes)
    {
        for (var first = 0; first < boxes.Length; first++)
        {
            for (var second = first + 1; second < boxes.Length; second++)
            {
                yield return (boxes[first], boxes[second]);
            }
        }
    }

    // ---- the budget, and the shapes that need no arranging -------------------------------------

    /// <summary>
    /// Requirement 6.4, the memory ground. Answered by not starting rather than by running slowly:
    /// the refusal names both numbers, because a user told only "too large" cannot tell whether
    /// they are ten variables over or ten thousand.
    /// </summary>
    [Fact]
    public void ADocumentOverTheBudget_IsRefusedWithItsSize_RatherThanArrangedSlowly()
    {
        // Arrange.
        var model = Ring(40);

        // Act.
        var result = SelfOrganizingLayout.Compute(model, budget: 25);

        // Assert.
        Assert.False(result.IsArranged);
        Assert.Equal(40, result.Size);
        Assert.Contains("40 variables", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("25", result.Refusal, StringComparison.Ordinal);
        Assert.Empty(result.Boxes);
    }

    [Fact]
    public void AnEmptyDocument_IsArrangedWithNothingInIt()
    {
        // Act.
        var result = SelfOrganizingLayout.Compute(Parse("causal-loop 1\r\n"));

        // Assert.
        Assert.True(result.IsArranged);
        Assert.Empty(result.Boxes);
    }

    /// <summary>
    /// One variable has nothing to organize itself against. Presenting stimuli would only drag it
    /// toward whichever corner the sequence favoured, so it stays where the spiral put it — at the
    /// origin, which is a real position and not a missing one (Requirement 7.1).
    /// </summary>
    [Fact]
    public void ASingleVariable_StaysAtTheOrigin_RatherThanDriftingTowardTheStimuli()
    {
        // Act.
        var result = SelfOrganizingLayout.Compute(Parse(
            "causal-loop 1\r\nvariable alone \"Alone\"\r\n"));

        // Assert.
        var box = Assert.Single(result.Boxes).Value;
        Assert.Equal(0, box.CenterX, 9);
        Assert.Equal(0, box.CenterY, 9);
    }

    [Fact]
    public void EveryDeclaredVariable_IsPlaced()
    {
        // Arrange.
        var model = Ring(20);

        // Act.
        var boxes = SelfOrganizingLayout.Compute(model).Boxes;

        // Assert.
        // Requirement 7.4: the claim below is vacuously true of a layout that placed nothing.
        Assert.NotEmpty(boxes);
        Assert.All(model.Variables, variable => Assert.True(boxes.ContainsKey(variable.Id)));
    }
}
