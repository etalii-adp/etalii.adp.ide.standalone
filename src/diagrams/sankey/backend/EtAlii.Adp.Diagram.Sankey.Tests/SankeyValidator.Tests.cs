using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>The rules, each reported on the line it breaks, and none reported for a clean document.</summary>
public sealed class SankeyValidatorTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void EveryRule_IsReportedOnTheBrokenDocument()
    {
        // Act.
        var breaches = SankeyValidator.Validate(SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(Fixture("rules-broken.skv")))));

        // Assert.
        Assert.Equal(
            [
                SankeyRuleIds.BackwardFlow, SankeyRuleIds.DanglingFlow, SankeyRuleIds.DuplicateId, SankeyRuleIds.MissingId,
                SankeyRuleIds.MissingValue, SankeyRuleIds.NegativeValue, SankeyRuleIds.SelfFlow, SankeyRuleIds.UnknownColor,
            ],
            breaches.Select(breach => breach.RuleId).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(breaches, breach => breach.RuleId == SankeyRuleIds.DuplicateId && breach.Message.Contains("written twice", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnreadableEntry_IsAWarning_NotAnError()
    {
        // Act.
        var breaches = SankeyValidator.Validate(SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(Fixture("malformed-entries.skv")))));

        // Assert.
        Assert.All(breaches.Where(breach => breach.RuleId == SankeyRuleIds.UnreadableEntry), breach => Assert.True(breach.IsWarning));
        Assert.NotEmpty(breaches);
    }

    [Fact]
    public async Task ABreach_ReachesThePanel_OnItsOneBasedLine()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Fixture("rules-broken.skv"), TestContext.Current.CancellationToken);
        var lines = text.Split('\n').ToList();
        var selfFlowLine = lines.FindIndex(line => line.Contains("from: b", StringComparison.Ordinal)) + 1;

        // Act.
        var problems = await new SankeyValidator().ValidateAsync(new DiagramValidationRequest(text, "rules-broken", "", Fixture("rules-broken.skv"), null), TestContext.Current.CancellationToken);

        // Assert.
        var selfFlow = Assert.Single(problems, problem => problem.RuleId == SankeyRuleIds.SelfFlow);
        Assert.Equal(DiagramProblemSeverity.Error, selfFlow.Severity);
        Assert.Equal((uint)selfFlowLine, Assert.IsType<DiagramProblemLineLocation>(selfFlow.Location).Number);
        Assert.Equal(DiagramProblemSeverity.Warning, problems.First(problem => problem.RuleId == SankeyRuleIds.BackwardFlow).Severity);
    }

    public static TheoryData<string> CleanDocuments =>
        [Fixture("lf-line-endings.skv"), SankeyExamples.BrightwaterCoffee, SankeyExamples.UkEnergy, SankeyExamples.RecentGraduates];

    [Theory]
    [MemberData(nameof(CleanDocuments))]
    public void ACleanDocument_ReportsNothing(string path)
    {
        // Act.
        var breaches = SankeyValidator.Validate(SankeyParser.Parse(LineDocument.Parse(File.ReadAllText(path))));

        // Assert.
        Assert.Empty(breaches);
    }
}
