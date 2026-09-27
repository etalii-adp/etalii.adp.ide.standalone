using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// What every shipped example must get right about time: an influence joins the phases its two trends
/// were in when it acted (Peter's review of technology-trends, 2026-09-27).
/// </summary>
public class GhgExampleInfluencePhaseTests
{
    public static TheoryData<string> Examples => ["technology-trends", "digital-trends", "energy-breakthroughs", "llms-and-agents", "coal-technologies"];

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

    /// <summary>Peter asked for at least one empty row between any two rows that hold trends.</summary>
    [Fact]
    public void TechnologyTrends_LeavesAnEmptyRowBetweenEveryTwoRowsOfTrends()
    {
        var rows = Model("technology-trends").Trends.Select(trend => trend.Row).Distinct().Order().ToList();

        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 2, $"Rows {pair.First} and {pair.Second} both hold trends with no empty row between them."));
    }

    private static (int Start, int Stop) PhaseSpan(GhgTrend trend, int phase)
    {
        IReadOnlyList<int> edges = [trend.Start!.Value, .. GhgPhases.BoundariesOf(trend), trend.Stop!.Value];
        return (edges[phase], edges[phase + 1]);
    }
}
