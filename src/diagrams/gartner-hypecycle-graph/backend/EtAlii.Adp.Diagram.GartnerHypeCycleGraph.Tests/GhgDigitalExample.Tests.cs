using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// The digital-trends example, held to what its readme claims - every claim asserted from the parsed
/// model rather than by eye, as <see cref="GhgExampleTests"/> does for technology-trends.
/// </summary>
public class GhgDigitalExampleTests
{
    private static LineDocument Document() => LineDocument.Parse(File.ReadAllText(GhgModuleFiles.ExampleNamed("digital-trends")));

    private static GhgModel Model() => GhgParser.Parse(Document());

    public static TheoryData<int> EveryPhaseCount => [1, 2, 3, 4];

    [Fact]
    public void TheExample_ReadsWithoutAProblem_AndIsThirtyTrends()
    {
        var model = Model();

        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Problems);
        Assert.Equal(30, model.Trends.Count);
        Assert.Equal(52, model.Influences.Count);
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

    [Theory]
    [MemberData(nameof(EveryPhaseCount))]
    public void EveryPhaseCount_Occurs(int phases)
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
            // A trigger has no phases, so an influence from one leaves nothing that could be hidden.
            Assert.True(!trends.TryGetValue(influence.From, out var from) || influence.FromEnd.PhaseIndex < from.VisiblePhases, $"{influence.Id} leaves a phase {influence.From} does not show.");
            Assert.True(influence.ToEnd.PhaseIndex < trends[influence.To].VisiblePhases, $"{influence.Id} lands on a phase {influence.To} does not show.");
        });
    }

    /// <summary>The readme says no two trends in a row overlap in time.</summary>
    [Fact]
    public void NoTwoTrendsInARow_Overlap()
    {
        foreach (var row in Model().Trends.GroupBy(trend => trend.Row))
        {
            var ordered = row.OrderBy(trend => trend.Start).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                Assert.True(ordered[index].Start >= ordered[index - 1].Stop, $"{ordered[index - 1].Id} and {ordered[index].Id} overlap on row {row.Key}.");
            }
        }
    }

    [Theory]
    [InlineData("ai")]
    [InlineData("communication and computing")]
    [InlineData("media or commerce")]
    public void EachFilterTheReadmeNames_MatchesSomeTrends_AndHidesSome(string filter)
    {
        Func<IReadOnlyList<string>, bool> matches = filter switch
        {
            "ai" => tags => tags.Contains("ai"),
            "communication and computing" => tags => tags.Contains("communication") && tags.Contains("computing"),
            "media or commerce" => tags => tags.Contains("media") || tags.Contains("commerce"),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var trends = Model().Trends;

        Assert.Contains(trends, trend => matches(trend.Tags));
        Assert.Contains(trends, trend => !matches(trend.Tags));
    }

    [Fact]
    public void TheDraggedBoundaries_AreOnTheThreeTrendsTheReadmeNames()
    {
        var dragged = Model().Trends.Where(trend => trend.DraggedEnds.Any(end => end is not null)).Select(trend => trend.Id).Order();

        Assert.Equal(["blockchain", "dot-com-bubble", "virtual-reality"], dragged);
    }
}
