using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 16: one fixture per rule id carrying exactly that breach, and every breaching document still
/// opens (Requirement 2.4).
/// </summary>
public class GhgRulesTests
{
    private static LineDocument Load(string name) =>
        LineDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    public static TheoryData<string, string> EveryRule => new()
    {
        { GhgRuleIds.DuplicateInfluence, "rule-duplicate-influence.ghg" },
        { GhgRuleIds.SelfInfluence, "rule-self-influence.ghg" },
        { GhgRuleIds.StopBeforeStart, "rule-stop-before-start.ghg" },
        { GhgRuleIds.PhaseCount, "rule-phase-count.ghg" },
        { GhgRuleIds.BoundaryOrder, "rule-boundary-order.ghg" },
        { GhgRuleIds.BadAttachment, "rule-bad-attachment.ghg" },
        { GhgRuleIds.DanglingReference, "rule-dangling-reference.ghg" },
        { GhgRuleIds.DuplicateId, "rule-duplicate-id.ghg" },
        { GhgRuleIds.UnreadableEntry, "rule-unreadable-entry.ghg" },
    };

    /// <summary>Each fixture breaks exactly its rule, and nothing else - and still opens with its trends.</summary>
    [Theory]
    [MemberData(nameof(EveryRule))]
    public void EachFixture_BreaksExactlyItsRule_AndStillOpens(string ruleId, string fixture)
    {
        var model = GhgParser.Parse(Load(fixture));

        var breaches = GhgValidator.Validate(model);

        Assert.True(
            breaches.Count > 0 && breaches.All(breach => breach.RuleId == ruleId),
            $"{fixture}: expected only {ruleId}, found " + string.Join("; ", breaches.Select(breach => $"{breach.RuleId}: {breach.Message}")));
        Assert.NotEmpty(model.Trends);
    }

    [Fact]
    public void TheNineRuleIds_EachHaveAFixture()
    {
        Assert.Equal(GhgRuleIds.All.Order(StringComparer.Ordinal), EveryRule.Select(row => row.Data.Item1).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ACleanDocument_BreaksNothing()
    {
        Assert.Empty(GhgValidator.Validate(Load("rules-clean.ghg")));
    }

    /// <summary>
    /// The user's ruling Q3: one influence per DIRECTION. A -&gt; B together with B -&gt; A is NOT a
    /// duplicate - the case a duplicate check that treats the pair as unordered reports.
    /// </summary>
    [Fact]
    public void OneInfluenceEachWay_IsNotADuplicate()
    {
        var model = GhgParser.Parse(Load("rule-opposite-directions.ghg"));
        Assert.Equal(2, model.Influences.Count);

        Assert.Empty(GhgValidator.Validate(model));
    }

    /// <summary>Requirement 7.3: a duplicate whose first copy is on a hidden phase is still a duplicate.</summary>
    [Fact]
    public void ADuplicateOfAHiddenInfluence_IsStillReported()
    {
        var model = GhgParser.Parse(Load("rule-duplicate-hidden.ghg"));
        var a = model.Trends.Single(trend => trend.Id == "a");
        var hidden = model.Influences.Single(influence => influence.Id == "ab");
        Assert.True(hidden.FromEnd.PhaseIndex >= a.VisiblePhases, "the fixture's first copy must be on a hidden phase");

        var breach = Assert.Single(GhgValidator.Validate(model));

        Assert.Equal(GhgRuleIds.DuplicateInfluence, breach.RuleId);
    }

    /// <summary>The panel seam: every breach reaches the Errors and Warnings panel with its rule and line.</summary>
    [Fact]
    public async Task TheValidator_CarriesEachBreachToThePanel()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rule-self-influence.ghg"));

        var problems = await new GhgValidator().ValidateAsync(
            new DiagramValidationRequest(text, "rule-self-influence", "/root", "/root/rule-self-influence.ghg", null),
            TestContext.Current.CancellationToken);

        var problem = Assert.Single(problems);
        Assert.Equal(GhgRuleIds.SelfInfluence, problem.RuleId);
        Assert.IsType<DiagramProblemLineLocation>(problem.Location);
    }
}
