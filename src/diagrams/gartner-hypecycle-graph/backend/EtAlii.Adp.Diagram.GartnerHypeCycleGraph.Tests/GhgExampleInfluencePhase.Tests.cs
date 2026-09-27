using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// What every shipped example must get right about time: an influence joins the phases its two trends
/// were in when it acted (Peter's review of technology-trends, 2026-09-27).
/// </summary>
public class GhgExampleInfluencePhaseTests
{
    public static TheoryData<string> Examples => ["technology-trends", "digital-trends", "energy-breakthroughs", "llms-and-agents", "eras-of-innovation"];

    private static GhgModel Model(string name) => GhgParser.Parse(LineDocument.Parse(File.ReadAllText(GhgModuleFiles.ExampleNamed(name))));

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
            var from = trends[influence.From];
            var to = trends[influence.To];
            if (influence.FromEnd.PhaseIndex >= from.VisiblePhases || influence.ToEnd.PhaseIndex >= to.VisiblePhases)
            {
                continue; // hidden, and asserted as such where an example claims one
            }

            var (fromStart, fromStop) = PhaseSpan(from, influence.FromEnd.PhaseIndex);
            var (toStart, toStop) = PhaseSpan(to, influence.ToEnd.PhaseIndex);
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

        foreach (var influence in Drawn(model, trends))
        {
            var leaves = DateOf(trends[influence.From], influence.FromEnd);
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
            .GroupBy(end => (end.Trend, end.End.Phase, end.End.Edge))
            .Where(slot => slot.Count() >= 2)
            .Where(slot => slot.Max(end => end.End.At!.Value) - slot.Min(end => end.End.At!.Value) < 0.2)
            .Select(slot => $"{slot.Key.Trend} {slot.Key.Phase}/{slot.Key.Edge} ({string.Join(", ", slot.Select(end => end.End.At))})")
            .ToList();

        Assert.True(stacked.Count == 0, $"These phase edges stack their influences in one spot: {string.Join("; ", stacked)}.");
    }

    /// <summary>Peter asked for at least one empty row between any two rows that hold trends.</summary>
    [Fact]
    public void TechnologyTrends_LeavesAnEmptyRowBetweenEveryTwoRowsOfTrends()
    {
        var rows = Model("technology-trends").Trends.Select(trend => trend.Row).Distinct().Order().ToList();

        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 2, $"Rows {pair.First} and {pair.Second} both hold trends with no empty row between them."));
    }

    /// <summary>The influences drawn: both ends readable and on a visible phase.</summary>
    private static IEnumerable<GhgInfluence> Drawn(GhgModel model, Dictionary<string, GhgTrend> trends) =>
        model.Influences.Where(influence =>
            influence.FromEnd.IsReadable && influence.ToEnd.IsReadable &&
            influence.FromEnd.PhaseIndex < trends[influence.From].VisiblePhases &&
            influence.ToEnd.PhaseIndex < trends[influence.To].VisiblePhases);

    /// <summary>The date under an end: its fraction along its phase's span.</summary>
    private static double DateOf(GhgTrend trend, GhgEnd end)
    {
        var (start, stop) = PhaseSpan(trend, end.PhaseIndex);
        return start + (end.At!.Value * (stop - start));
    }

    private static (int Start, int Stop) PhaseSpan(GhgTrend trend, int phase)
    {
        IReadOnlyList<int> edges = [trend.Start!.Value, .. GhgPhases.BoundariesOf(trend), trend.Stop!.Value];
        return (edges[phase], edges[phase + 1]);
    }
}
