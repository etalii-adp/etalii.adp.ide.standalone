using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// What every shipped example must get right about time: an influence joins the phases its two trends
/// were in when it acted (Peter's review of technology-trends, 2026-09-27).
/// </summary>
public class GhgExampleInfluencePhaseTests
{
    public static TheoryData<string> Examples => ["technology-trends", "digital-trends", "energy-breakthroughs", "llms-and-agents", "eras-of-innovation", "coal-technologies", "electric-vehicles", "internet-evolution", "warfare-in-ukraine"];

    private static GhgModel Model(string name) => GhgParser.Parse(GhgBody.Parse(File.ReadAllText(GhgModuleFiles.ExampleNamed(name))));

    /// <summary>
    /// The two phases an influence joins overlap in time. The one exception is a trend that ended before
    /// the other began: it acts from its last drawn phase on the other's Peak.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryInfluence_JoinsPhasesThatMeetInTime(string name)
    {
        var model = Model(name);
        var trends = model.Trends.ToDictionary(trend => trend.Id);
        var wrong = new List<string>();

        foreach (var influence in model.Influences)
        {
            if (!trends.TryGetValue(influence.From, out var from))
            {
                continue; // a trigger's influences are EveryInfluenceFromATrigger_ActsOnAPhaseNotYetOver's
            }

            var to = trends[influence.To];
            if (influence.FromEnd.PhaseIndex >= from.VisiblePhases || influence.ToEnd.PhaseIndex >= to.VisiblePhases)
            {
                continue; // hidden, and asserted as such where an example claims one
            }

            (int fromStart, int fromStop) = PhaseSpan(from, influence.FromEnd.PhaseIndex);
            (int toStart, int toStop) = PhaseSpan(to, influence.ToEnd.PhaseIndex);
            var meet = Math.Max(fromStart, toStart) < Math.Min(fromStop, toStop);
            var legacy = from.Stop <= to.Start && influence.FromEnd.PhaseIndex == from.VisiblePhases - 1 && influence.ToEnd.PhaseIndex == 0;
            if (!meet && !legacy)
            {
                wrong.Add($"{influence.Id} ({influence.FromEnd.Phase} {GhgScale.FormatMonth(fromStart)} to {GhgScale.FormatMonth(fromStop)}, {influence.ToEnd.Phase} {GhgScale.FormatMonth(toStart)} to {GhgScale.FormatMonth(toStop)})");
            }
        }

        Assert.True(wrong.Count == 0, $"These influences join phases that never meet in time: {string.Join("; ", wrong)}.");
    }

    /// <summary>
    /// An influence reaches its effect no earlier than it leaves its cause: the date under its From
    /// end is at or before the date under its To end (Peter, 2026-09-27: "take into consideration
    /// logical order of how influences could have happened").
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryInfluence_ArrivesNoEarlierThanItLeaves(string name)
    {
        var model = Model(name);
        var trends = model.Trends.ToDictionary(trend => trend.Id);
        var wrong = new List<string>();

        var triggers = model.Triggers.ToDictionary(trigger => trigger.Id);
        foreach (var influence in Drawn(model, trends))
        {
            var leaves = triggers.TryGetValue(influence.From, out var trigger) ? trigger.Date!.Value : DateOf(trends[influence.From], influence.FromEnd);
            var arrives = DateOf(trends[influence.To], influence.ToEnd);
            if (arrives < leaves)
            {
                wrong.Add($"{influence.Id} (leaves {GhgScale.FormatMonth((int)leaves)}, arrives {GhgScale.FormatMonth((int)arrives)})");
            }
        }

        Assert.True(wrong.Count == 0, $"These influences arrive before they leave: {string.Join("; ", wrong)}.");
    }

    /// <summary>
    /// The ends sharing one edge of one phase are spread across it rather than stacked at one spot:
    /// they cover at least a fifth of the phase (Peter, 2026-09-27: "spread the influence relations
    /// anchor positions more across a phase").
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void TheEndsOnAPhaseEdge_AreSpreadAcrossIt(string name)
    {
        var model = Model(name);
        var trends = model.Trends.ToDictionary(trend => trend.Id);
        var stacked = Drawn(model, trends)
            .SelectMany(influence => new[] { (Trend: influence.From, End: influence.FromEnd), (Trend: influence.To, End: influence.ToEnd) })
            .Where(end => !end.End.IsNone)
            .GroupBy(end => (end.Trend, end.End.Phase, end.End.Edge))
            .Where(slot => slot.Count() >= 2)
            .Where(slot => slot.Max(end => end.End.At!.Value) - slot.Min(end => end.End.At!.Value) < 0.2)
            .Select(slot => $"{slot.Key.Trend} {slot.Key.Phase}/{slot.Key.Edge} ({string.Join(", ", slot.Select(end => end.End.At))})")
            .ToList();

        Assert.True(stacked.Count == 0, $"These phase edges stack their influences in one spot: {string.Join("; ", stacked)}.");
    }

    /// <summary>
    /// A trigger acts on a trend in the phase the trend was in when it happened, or a later one: the
    /// phase it lands on is not over before the trigger's date. A trigger before a trend began acts on
    /// its Peak.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryInfluenceFromATrigger_ActsOnAPhaseNotYetOver(string name)
    {
        var model = Model(name);
        var trends = model.Trends.ToDictionary(trend => trend.Id);
        var wrong = new List<string>();

        foreach (var trigger in model.Triggers)
        {
            foreach (var influence in model.Influences.Where(influence => influence.From == trigger.Id))
            {
                (_, int stop) = PhaseSpan(trends[influence.To], influence.ToEnd.PhaseIndex);
                if (stop <= trigger.Date!.Value)
                {
                    wrong.Add($"{influence.Id} ({trigger.Id} at {GhgScale.FormatMonth(trigger.Date.Value)}, {influence.ToEnd.Phase} over by {GhgScale.FormatMonth(stop)})");
                }
            }
        }

        Assert.True(wrong.Count == 0, $"These influences act on a phase that was already over: {string.Join("; ", wrong)}.");
    }

    /// <summary>
    /// ghg-triggers-and-notes Requirement 10.1: every shipped example shows at least one real trigger
    /// setting a trend off, and validates without a report.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryExample_HasATriggerWithAnInfluence_AndBreaksNoRule(string name)
    {
        var model = Model(name);

        Assert.Contains(model.Triggers, trigger => model.Influences.Any(influence => influence.From == trigger.Id));
        Assert.Empty(GhgValidator.Validate(model));
    }

    /// <summary>
    /// Every example ships twice - beside the module and under <c>src/examples</c> - and the two copies
    /// are one document, byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void BothCopiesOfAnExample_AreByteIdentical(string name)
    {
        var shipped = GhgModuleFiles.ShippedExampleNamed(name);

        Assert.Equal(File.ReadAllBytes(GhgModuleFiles.ExampleNamed(name)), File.ReadAllBytes(shipped));
    }

    /// <summary>ghg-triggers-and-notes Requirement 10.5: at least one example carries a note.</summary>
    [Fact]
    public void AtLeastOneExample_CarriesANote()
    {
        Assert.Contains(Examples, row => Model(row.Data).Notes.Count > 0);
    }

    /// <summary>Peter asked for at least one empty row between any two rows that hold trends.</summary>
    [Fact]
    public void TechnologyTrends_LeavesAnEmptyRowBetweenEveryTwoRowsOfTrends()
    {
        var rows = Model("technology-trends").Trends.Select(trend => trend.Row).Distinct().Order().ToList();

        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 2, $"Rows {pair.First} and {pair.Second} both hold trends with no empty row between them."));
    }

    /// <summary>The influences drawn: both ends readable and on a visible phase, a trigger's end being none.</summary>
    private static IEnumerable<GhgInfluence> Drawn(GhgModel model, Dictionary<string, GhgTrend> trends) =>
        model.Influences.Where(influence =>
            (influence.FromEnd.IsNone || (influence.FromEnd.IsReadable && influence.FromEnd.PhaseIndex < trends[influence.From].VisiblePhases)) &&
            influence.ToEnd.IsReadable &&
            influence.ToEnd.PhaseIndex < trends[influence.To].VisiblePhases);

    /// <summary>The date under an end: its fraction along its phase's span.</summary>
    private static double DateOf(GhgTrend trend, GhgEnd end)
    {
        (int start, int stop) = PhaseSpan(trend, end.PhaseIndex);
        return start + (end.At!.Value * (stop - start));
    }

    private static (int Start, int Stop) PhaseSpan(GhgTrend trend, int phase)
    {
        IReadOnlyList<int> edges = [trend.Start!.Value, .. GhgPhases.BoundariesOf(trend), trend.Stop!.Value];
        return (edges[phase], edges[phase + 1]);
    }
}
