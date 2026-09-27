using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 21: the technology-trends example (Requirement 10.3), held to what its readme and the
/// requirement claim - every claim asserted from the parsed model rather than by eye.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are not fixtures.</b> The fixtures each carry one construct or one breach; the example is
/// a document a reader opens, and what is tested here is only that the claims made about it stay true.
/// </para>
/// <para>
/// <b>The phase and edge cases walk <see cref="GhgPhases.Names"/></b> rather than a list written here.
/// </para>
/// </remarks>
public class GhgExampleTests
{
    private static LineDocument Document() => LineDocument.Parse(File.ReadAllText(GhgModuleFiles.Example));

    private static GhgModel Model() => GhgParser.Parse(Document());

    public static TheoryData<int> EveryPhaseCount => [1, 2, 3, 4];

    public static TheoryData<string, string> EveryPhaseAndEdge => new()
    {
        { GhgPhases.Names[0], GhgEnd.Top }, { GhgPhases.Names[0], GhgEnd.Bottom },
        { GhgPhases.Names[1], GhgEnd.Top }, { GhgPhases.Names[1], GhgEnd.Bottom },
        { GhgPhases.Names[2], GhgEnd.Top }, { GhgPhases.Names[2], GhgEnd.Bottom },
        { GhgPhases.Names[3], GhgEnd.Top }, { GhgPhases.Names[3], GhgEnd.Bottom },
    };

    [Fact]
    public void TheExample_ReadsWithoutAProblem_AndIsAboutTwoHundredTrends()
    {
        var model = Model();

        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Problems);
        Assert.InRange(model.Trends.Count, 180, 220);
    }

    /// <summary>The validator reports nothing for the example.</summary>
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

    /// <summary>
    /// Influences attach to each phase on both its top and its bottom edge - as an end, from or to, on
    /// a phase its trend draws.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPhaseAndEdge))]
    public void EveryPhase_HasAnInfluenceEnd_OnEachEdge(string phase, string edge)
    {
        var model = Model();
        var trends = model.Trends.ToDictionary(trend => trend.Id);

        bool Drawn(string trendId, GhgEnd end) =>
            end.Phase == phase && end.Edge == edge && end.PhaseIndex < trends[trendId].VisiblePhases;

        Assert.Contains(model.Influences, influence => Drawn(influence.From, influence.FromEnd) || Drawn(influence.To, influence.ToEnd));
    }

    [Fact]
    public void AtLeastOneInfluence_IsHiddenByAPhaseCount()
    {
        var model = Model();
        var trends = model.Trends.ToDictionary(trend => trend.Id);

        Assert.Contains(model.Influences, influence =>
            influence.FromEnd.PhaseIndex >= trends[influence.From].VisiblePhases ||
            influence.ToEnd.PhaseIndex >= trends[influence.To].VisiblePhases);
    }

    /// <summary>
    /// The three filters the readme names each match some trends and hide the rest. The client parses
    /// the expressions; here each is written out as the tag sets it means.
    /// </summary>
    [Theory]
    [InlineData("energy")]
    [InlineData("communication and computing")]
    [InlineData("transport or energy")]
    public void EachFilterTheReadmeNames_MatchesSomeTrends_AndHidesSome(string filter)
    {
        Func<IReadOnlyList<string>, bool> matches = filter switch
        {
            "energy" => tags => tags.Contains("energy"),
            "communication and computing" => tags => tags.Contains("communication") && tags.Contains("computing"),
            "transport or energy" => tags => tags.Contains("transport") || tags.Contains("energy"),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var trends = Model().Trends;

        Assert.Contains(trends, trend => matches(trend.Tags));
        Assert.Contains(trends, trend => !matches(trend.Tags));
    }

    [Fact]
    public void Descriptions_AreOnTrendsAndOnInfluences()
    {
        var model = Model();

        Assert.Contains(model.Trends, trend => trend.Description.Length > 0);
        Assert.Contains(model.Influences, influence => influence.Description.Length > 0);
    }
}
