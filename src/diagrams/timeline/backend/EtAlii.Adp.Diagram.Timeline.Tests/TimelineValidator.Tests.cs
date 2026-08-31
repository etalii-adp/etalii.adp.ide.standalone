using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The seam itself, as distinct from the rules it carries: parse, then judge (Requirement 12.1).
/// </summary>
public class TimelineValidatorTests
{
    private static ValueTask<IReadOnlyList<DiagramProblem>> JudgeAsync(string document) =>
        new TimelineValidator().ValidateAsync(
            new DiagramValidationRequest(document, "plan", "/root", "/root/plan.tml", null),
            CancellationToken.None);

    [Fact]
    public async Task ACorrectTimeline_ReachesTheRulesAndSurvivesThem()
    {
        // Arrange & act.
        var problems = await JudgeAsync("timeline: 1\nelements:\n  - id: aaa\n    begin: 2026-01-01\n    row: 0\n");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AFlawedTimeline_ReportsTheRuleThatFired()
    {
        // Arrange & act.
        // The wording is TimelineRuleSetTests' business; this proves the document got there.
        var problems = await JudgeAsync("timeline: 1\nelements:\n  - id: aaa\n    begin: 2026-02-01\n    end: 2026-01-01\n    row: 0\n");

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == TimelineRules.EndBeforeBegin);
    }

    [Fact]
    public async Task AFileThatIsNotYaml_IsOneErrorNamingItsLine()
    {
        // Arrange & act.
        var problems = await JudgeAsync("elements: [\n  - id: a\n");

        // Assert.
        // One problem, not a pile of consequences from an empty model.
        var problem = Assert.Single(problems);
        Assert.Equal(TimelineValidator.UnparseableRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.True(Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number >= 1);
    }

    [Fact]
    public async Task AnEmptyFile_IsJudgedRatherThanCrashed()
    {
        // Arrange & act.
        var problems = await JudgeAsync("");

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == TimelineValidator.UnparseableRuleId);
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        Assert.Equal(Diagram.Timeline.Origin, new TimelineValidator().Origin);
    }
}
