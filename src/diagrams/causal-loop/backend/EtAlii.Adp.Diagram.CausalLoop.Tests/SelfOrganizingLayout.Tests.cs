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

        var start = new ProcessStartInfo(host!)
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
        File.WriteAllText(output!, Serialize(SelfOrganizingLayout.Compute(Fixture())));
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
