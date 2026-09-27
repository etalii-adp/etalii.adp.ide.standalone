using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// The eras-of-innovation example, held to what its readme claims - every claim asserted from the
/// parsed model rather than by eye, as <see cref="GhgDigitalExampleTests"/> does for digital-trends.
/// </summary>
public class GhgErasExampleTests
{
    private static LineDocument Document() => LineDocument.Parse(File.ReadAllText(GhgModuleFiles.ExampleNamed("eras-of-innovation")));

    private static GhgModel Model() => GhgParser.Parse(Document());

    public static TheoryData<int> EveryPhaseCount => [1, 2, 3, 4];

    [Fact]
    public void TheExample_ReadsWithoutAProblem_InDecades()
    {
        var model = Model();

        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Problems);
        Assert.Same(GhgTimeUnit.Decade, model.TimeUnit);
        Assert.Equal(42, model.Trends.Count);
        Assert.Equal(55, model.Influences.Count);
    }

    [Fact]
    public void TheValidator_ReportsNothingForTheExample()
    {
        var breaches = GhgValidator.Validate(Document());

        Assert.True(
            breaches.Count == 0,
            "The example breaks the rules it exists to demonstrate: "
            + string.Join("; ", breaches.Select(breach => $"{breach.RuleId} at line {breach.Line + 1}: {breach.Message}")));
    }

    /// <summary>The readme says it spans the years -3500 to 2050, written as ISO 8601 years.</summary>
    [Fact]
    public void TheExample_SpansTheYearsMinus3500To2050()
    {
        var trends = Model().Trends;

        Assert.Equal("-3500-01", GhgScale.FormatMonth(trends.Min(trend => trend.Start!.Value)));
        Assert.Equal("2050-01", GhgScale.FormatMonth(trends.Max(trend => trend.Stop!.Value)));
    }

    [Theory]
    [MemberData(nameof(EveryPhaseCount))]
    public void EachPhaseCountTheReadmeNames_Occurs(int phases)
    {
        Assert.Contains(Model().Trends, trend => trend.Phases == phases);
    }

    /// <summary>The readme says no influence is hidden: each attaches to a phase both its trends show.</summary>
    [Fact]
    public void NoInfluence_IsHiddenByAPhaseCount()
    {
        var model = Model();
        var trends = model.Trends.ToDictionary(trend => trend.Id);

        Assert.All(model.Influences, influence =>
        {
            Assert.True(influence.FromEnd.PhaseIndex < trends[influence.From].VisiblePhases, $"{influence.Id} leaves a phase {influence.From} does not show.");
            Assert.True(influence.ToEnd.PhaseIndex < trends[influence.To].VisiblePhases, $"{influence.Id} lands on a phase {influence.To} does not show.");
        });
    }

    /// <summary>
    /// The readme says trends share a row only where the later one's label, drawn before it, clears the
    /// earlier one's end - about six and a half units a character at the label's twelve-unit font.
    /// </summary>
    [Fact]
    public void TrendsShareARow_OnlyWhereTheLaterLabelClearsTheEarlierTrend()
    {
        foreach (var row in Model().Trends.GroupBy(trend => trend.Row))
        {
            var ordered = row.OrderBy(trend => trend.Start).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                var gap = GhgScale.XOf(ordered[index].Start!.Value, GhgTimeUnit.Decade) - GhgScale.XOf(ordered[index - 1].Stop!.Value, GhgTimeUnit.Decade);
                Assert.True(gap >= ordered[index].Name.Length * 6.5, $"{ordered[index].Id}'s label would overlap {ordered[index - 1].Id} on row {row.Key}.");
            }
        }
    }

    /// <summary>As in technology-trends, at least one empty row between any two rows that hold trends.</summary>
    [Fact]
    public void AnEmptyRow_LiesBetweenEveryTwoRowsOfTrends()
    {
        var rows = Model().Trends.Select(trend => trend.Row).Distinct().Order().ToList();

        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 2, $"Rows {pair.First} and {pair.Second} both hold trends with no empty row between them."));
    }

    [Theory]
    [InlineData("knowledge")]
    [InlineData("energy or transport")]
    [InlineData("health and not knowledge")]
    public void EachFilterTheReadmeNames_MatchesSomeTrends_AndHidesSome(string filter)
    {
        Func<IReadOnlyList<string>, bool> matches = filter switch
        {
            "knowledge" => tags => tags.Contains("knowledge"),
            "energy or transport" => tags => tags.Contains("energy") || tags.Contains("transport"),
            "health and not knowledge" => tags => tags.Contains("health") && !tags.Contains("knowledge"),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var trends = Model().Trends;

        Assert.Contains(trends, trend => matches(trend.Tags));
        Assert.Contains(trends, trend => !matches(trend.Tags));
    }
}
