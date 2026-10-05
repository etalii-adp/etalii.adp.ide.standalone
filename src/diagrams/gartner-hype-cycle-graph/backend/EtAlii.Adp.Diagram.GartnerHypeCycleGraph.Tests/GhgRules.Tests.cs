using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 16: one fixture per rule id carrying exactly that breach, and every breaching document still
/// opens (Requirement 2.4).
/// </summary>
public class GhgRulesTests
{
    private static GhgBody Load(string name) =>
        GhgBody.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

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
        { GhgRuleIds.InfluenceIntoTrigger, "rule-influence-into-trigger.ghg" },
        { GhgRuleIds.TriggerDate, "rule-trigger-date.ghg" },
        { GhgRuleIds.NotePosition, "rule-note-position.ghg" },
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
    public void EveryRuleId_HasAFixture()
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

    /// <summary>
    /// DISL 11.5.4: of the entries that share an id the first keeps it, and the second and later are
    /// still drawn, as ephemeral elements under an id of their own; the duplicate is still reported.
    /// </summary>
    [Fact]
    public void ALaterTrendReusingAnId_IsStillDrawn_AndTheFirstKeepsTheId()
    {
        var model = GhgParser.Parse(Load("rule-duplicate-id.ghg"));

        var drawn = new GhgElementMapper().Visible(model, DiagramViewport.Unbounded);

        Assert.Contains(GhgValidator.Validate(model), breach => breach.RuleId == GhgRuleIds.DuplicateId);
        var trends = drawn.Where(element => element.Type == GhgElementMapper.TrendType).ToList();
        Assert.Equal(3, trends.Count);
        Assert.Equal(3, trends.Select(trend => trend.Id).Distinct(StringComparer.Ordinal).Count());
        var first = trends.Single(trend => trend.Id == "a");
        var later = trends.Single(trend => trend.Id is not "a" and not "b");
        var width = GhgScale.WidthOf(GhgScale.MonthIndex(1920, 1) - GhgScale.MonthIndex(1900, 1), model.TimeUnit);
        Assert.Equal(GhgScale.XOf(GhgScale.MonthIndex(1900, 1), model.TimeUnit) + (width / 2), first.X, 6);
        var laterWidth = GhgScale.WidthOf(GhgScale.MonthIndex(1950, 1) - GhgScale.MonthIndex(1940, 1), model.TimeUnit);
        Assert.Equal(GhgScale.XOf(GhgScale.MonthIndex(1940, 1), model.TimeUnit) + (laterWidth / 2), later.X, 6);
    }

    /// <summary>DISL 11.5.4 across types: a trigger reusing a trend's id is drawn too, and the trend keeps the id.</summary>
    [Fact]
    public void ATriggerReusingATrendsId_IsStillDrawn()
    {
        const string text = "gartner-hypecycle-graph: 1\ntrends:\n  - id: same\n    name: A\n    start: 1900-01\n    stop: 1910-01\n    row: 0\n    phases: 4\ntriggers:\n  - id: same\n    name: T\n    date: 1899-01\n    row: 1\ninfluences: []\n";
        var model = GhgParser.Parse(GhgBody.Parse(text));

        var drawn = new GhgElementMapper().Visible(model, DiagramViewport.Unbounded);

        Assert.Equal("same", Assert.Single(drawn, element => element.Type == GhgElementMapper.TrendType).Id);
        Assert.NotEqual("same", Assert.Single(drawn, element => element.Type == GhgElementMapper.TriggerType).Id);
    }

    /// <summary>The panel seam: every breach reaches the Errors and Warnings panel with its rule and line.</summary>
    [Fact]
    public async Task TheValidator_CarriesEachBreachToThePanel()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rule-self-influence.ghg"), TestContext.Current.CancellationToken);

        var problems = await new GhgValidator().ValidateAsync(
            new DiagramValidationRequest(text, "rule-self-influence", "/root", "/root/rule-self-influence.ghg", null),
            TestContext.Current.CancellationToken);

        var problem = Assert.Single(problems);
        Assert.Equal(GhgRuleIds.SelfInfluence, problem.RuleId);
        Assert.IsType<DiagramProblemLineLocation>(problem.Location);
    }
}
