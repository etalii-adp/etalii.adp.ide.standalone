using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The user functions of the bundled hype cycle graph definition equal the code they describe, over
/// the parity corpus and over a grid of values the corpus does not reach: negative years, six-digit
/// years, every unit, every phase count and every arrangement of dragged boundaries. Where the two
/// differ, the code is the truth and the definition is fixed upstream.
/// </summary>
public class GhgDislFunctionsTests
{
    private static readonly long[] Years = [-123456, -3200, -1000, -1, 0, 1, 999, 1899, 1900, 1947, 2026, 9999, 10000, 123456];

    /// <summary>Every month of every year of the grid, and every month the corpus stores.</summary>
    private static IEnumerable<int> Months()
    {
        var corpus = GhgDisl.Corpus().SelectMany(document => document.Model.Trends.SelectMany(trend => new[] { trend.Start, trend.Stop }.Concat(trend.DraggedEnds))
            .Concat(document.Model.Triggers.Select(trigger => trigger.Date))
            .Concat(document.Model.Notes.Select(note => note.At)));
        return Years.SelectMany(year => Enumerable.Range(0, 12).Select(month => (int)((year * 12) + month)))
            .Concat(corpus.OfType<int>())
            .Distinct();
    }

    [Fact]
    public void MonthText_IsFormatMonth()
    {
        var months = Months().ToList();
        Assert.True(months.Count > 200);
        foreach (var month in months)
        {
            Assert.Equal(GhgScale.FormatMonth(month), GhgDisl.Evaluate("monthText(m)", new Dictionary<string, object?> { ["m"] = (long)month }));
        }
    }

    [Fact]
    public void FormatWhen_IsFormatWhenAndFormatWhenLong_InEveryUnit()
    {
        foreach (var month in Months())
        {
            foreach (var unit in GhgTimeUnit.All)
            {
                var variables = new Dictionary<string, object?> { ["m"] = (long)month, ["u"] = unit.Name };
                Assert.Equal(GhgScale.FormatWhen(month, unit), GhgDisl.Evaluate("formatWhen(m, u, false)", variables));
                Assert.Equal(GhgScale.FormatWhenLong(month, unit), GhgDisl.Evaluate("formatWhen(m, u, true)", variables));
            }
        }
    }

    [Fact]
    public void PhaseBoundaries_IsBoundariesOf_OverTheCorpus()
    {
        var trends = 0;
        foreach (var (path, model) in GhgDisl.Corpus())
        {
            var diagram = GhgDisl.DiagramOf(model);
            foreach (var (trend, element) in model.Trends.Zip(diagram.Nodes).Where(pair => pair.First.HasSpan))
            {
                AssertBoundaries(GhgPhases.BoundariesOf(trend), element, $"{path} {trend.Id}");
                trends++;
            }
        }
        Assert.True(trends > 50, $"Only {trends} trends with a span were compared.");
    }

    /// <summary>Every arrangement of three dragged boundaries - absent, before the start, at either end, inside, past the stop - for spans short and long, starts negative and positive, and phase counts in and out of range.</summary>
    [Fact]
    public void PhaseBoundaries_IsBoundariesOf_OverTheGrid()
    {
        int[] starts = [-38400, -1, 0, 24000];
        int[] spans = [1, 2, 3, 4, 5, 7, 100];
        int[] phases = [0, 1, 2, 3, 4, 5];
        var cases = 0;
        foreach (var start in starts)
        {
            foreach (var span in spans)
            {
                var stop = start + span;
                int?[] slots = [null, start - 1, start, start + 1, start + (span / 2), stop - 1, stop, stop + 3];
                foreach (var count in phases)
                {
                    foreach (var peak in slots)
                    {
                        foreach (var trough in slots)
                        {
                            foreach (var slope in slots)
                            {
                                var trend = new GhgTrend("t", "T", start, stop, 0, count, [peak, trough, slope], [], "", new(0, 0));
                                var element = GhgDisl.TrendOf(new DislDiagram(GhgDisl.Specification), trend);
                                AssertBoundaries(GhgPhases.BoundariesOf(trend), element, $"{start}..{stop} phases {count} dragged [{peak}, {trough}, {slope}]");
                                cases++;
                            }
                        }
                    }
                }
            }
        }
        Assert.Equal(starts.Length * spans.Length * phases.Length * 8 * 8 * 8, cases);
    }

    private static void AssertBoundaries(IReadOnlyList<int> expected, DislElement trend, string because)
    {
        var actual = (IReadOnlyList<object?>)GhgDisl.Evaluate("phaseBoundaries(t)", new Dictionary<string, object?> { ["t"] = trend })!;
        Assert.True(expected.Select(month => (object?)(long)month).SequenceEqual(actual), $"{because}: the code draws [{string.Join(", ", expected)}], the definition [{string.Join(", ", actual)}].");
    }

    [Fact]
    public void TooShort_IsTheSentenceGhgEditsRefusesWith()
    {
        for (var months = -3; months <= 12; months++)
        {
            for (var phases = -1; phases <= 6; phases++)
            {
                var expected = GhgEdits.TooShort(months, phases)?.Refusal ?? "";
                Assert.Equal(expected, GhgDisl.Evaluate("tooShort(months, phases)", new Dictionary<string, object?> { ["months"] = (long)months, ["phases"] = (long)phases }));
            }
        }
    }

    /// <summary>The boundary-order finding the definition states - its <c>when</c>, its <c>rule</c> and its message, built on <c>badBoundary</c> - is the one the code reports, trend by trend.</summary>
    [Fact]
    public void BadBoundary_GivesTheBoundaryOrderFindingOfTheCode()
    {
        var index = GhgDisl.RuleIndex("boundaryOrder");
        var findings = 0;

        foreach (var model in GhgDisl.Corpus().Select(document => document.Model).Concat(BoundaryGrid()))
        {
            var diagram = GhgDisl.DiagramOf(model);
            var expected = GhgRuleSet.Breaches(model).Where(breach => breach.RuleId == GhgRuleIds.BoundaryOrder).Select(breach => breach.Message).ToList();
            var actual = new List<string>();
            foreach (var trend in diagram.Nodes)
            {
                var variables = new Dictionary<string, object?> { ["self"] = trend, ["diagram"] = diagram, ["env"] = null };
                if (GhgDisl.EvaluateAt($"/constraints/rules/{index}/when", variables) is true
                    && GhgDisl.EvaluateAt($"/constraints/rules/{index}/rule", variables) is false)
                {
                    actual.Add((string)GhgDisl.EvaluateAt($"/constraints/rules/{index}/message/cel", variables)!);
                }
            }
            Assert.Equal(expected, actual);
            findings += expected.Count;
        }
        Assert.True(findings > 100, $"Only {findings} boundary-order findings were compared.");
    }

    /// <summary>One model per span and phase count, holding a trend for every arrangement of stored boundaries.</summary>
    private static IEnumerable<GhgModel> BoundaryGrid()
    {
        foreach (var (start, stop) in new[] { (-38400, -38390), (0, 3), (24000, 24120) })
        {
            int?[] slots = [null, start - 1, start, start + 1, (start + stop) / 2, stop - 1, stop, stop + 1];
            var trends = new List<GhgTrend>();
            foreach (var peak in slots)
            {
                foreach (var trough in slots)
                {
                    foreach (var slope in slots)
                    {
                        trends.Add(new GhgTrend($"t{trends.Count}", "T", start, stop, 0, 4, [peak, trough, slope], [], "", new(trends.Count, trends.Count)));
                    }
                }
            }
            yield return new GhgModel(trends, [], [], 1);
        }
    }

    [Fact]
    public void UniqueName_IsUniqueName()
    {
        List<IReadOnlyList<string>> takens =
        [
            [],
            ["Trend"],
            ["Trend", "Trend 2"],
            ["Trend", "Trend 3"],
            ["Trend 2"],
            ["Trend", "Trend 2", "Trend 3", "Trend 4", "Trend 5"],
            ["Trend", "Trend 2", "Trend 2", "Trend 3"],
            ["trend", "Trend "],
        ];
        foreach (var model in GhgDisl.Corpus().Select(document => document.Model))
        {
            takens.Add([.. model.Trends.Select(trend => trend.Name), .. model.Triggers.Select(trigger => trigger.Name)]);
        }

        var compared = 0;
        foreach (var taken in takens)
        {
            foreach (var name in taken.Append("Trend").Append("Trigger").Append("").Distinct())
            {
                var variables = new Dictionary<string, object?> { ["base"] = name, ["taken"] = taken.Cast<object?>().ToList() };
                Assert.Equal(GhgEdits.UniqueName(taken, name), GhgDisl.Evaluate("uniqueName(base, taken)", variables));
                compared++;
            }
        }
        Assert.True(compared > 100);
    }
}
