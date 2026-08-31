using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The mistakes a timeline hides, one test per rule, each from a plain YAML string with no
/// file, canvas or connection (Requirement 12.1).
/// </summary>
public class TimelineRuleSetTests
{
    private static IReadOnlyList<DiagramProblem> Judge(string yaml) =>
        TimelineRuleSet.Judge(TimelineParser.Parse(TimelineDocument.Parse(yaml)));

    private static IEnumerable<DiagramProblem> Of(string yaml, string ruleId) =>
        Judge(yaml).Where(problem => problem.RuleId == ruleId);

    private const string Healthy = """
        timeline: 1
        elements:
          - id: aaa
            label: Discovery
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: bbb
            label: Go
            begin: 2026-02-16T14:00:00
            row: 1
        connections:
          - id: ccc
            from: aaa
            to: bbb
            label: gates
        """;

    [Fact]
    public void ACorrectTimeline_HasNothingWrongWithIt()
    {
        // Arrange & act & assert.
        // The test that matters most: a rule that fires on a healthy file teaches the reader to
        // ignore the panel.
        Assert.Empty(Judge(Healthy));
    }

    [Fact]
    public void AnEmptyTimeline_IsAlsoFine()
    {
        // Assert.
        // A fresh document from the factory must open with a clean panel.
        Assert.Empty(Judge("timeline: 1\nelements: []\n"));
    }

    [Fact]
    public void EndBeforeBegin_IsAWarningNamingTheElement()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            timeline: 1
            elements:
              - id: aaa
                label: Backwards
                begin: 2026-02-01
                end: 2026-01-01
                row: 0
            """, TimelineRules.EndBeforeBegin));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("Backwards", problem.Message, StringComparison.Ordinal);
        Assert.Equal("aaa", Assert.IsType<DiagramProblemElementLocation>(problem.Location).Id);
    }

    [Fact]
    public void AnUnreadableTime_IsReported_AndTheElementStillCounts()
    {
        // Arrange.
        var yaml = """
            timeline: 1
            elements:
              - id: aaa
                label: Broken
                begin: not-a-date
                row: 0
              - id: bbb
                label: Fine
                begin: 2026-01-01
                row: 1
            """;

        // Act & assert.
        Assert.Single(Of(yaml, TimelineRules.UnreadableTime));
        // The rest of the diagram still draws - the broken element is warned about, not dropped.
        Assert.Equal(2, TimelineParser.Parse(TimelineDocument.Parse(yaml)).Elements.Count);
    }

    [Fact]
    public void AnUnreadableEnd_DoesNotAlsoFireEndBeforeBegin()
    {
        // Arrange & act.
        // A value nobody can read cannot be judged against the begin; piling a second warning on
        // the same broken value would be a consequence, not a finding.
        var yaml = "timeline: 1\nelements:\n  - id: aaa\n    begin: 2026-01-01\n    end: sideways\n    row: 0\n";

        // Assert.
        Assert.Single(Of(yaml, TimelineRules.UnreadableTime));
        Assert.Empty(Of(yaml, TimelineRules.EndBeforeBegin));
    }

    [Fact]
    public void ADanglingConnection_NamesTheMissingEnd()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            timeline: 1
            elements:
              - id: aaa
                begin: 2026-01-01
                row: 0
            connections:
              - id: ccc
                from: aaa
                to: ghost
            """, TimelineRules.DanglingConnection));

        // Assert.
        Assert.Contains("ghost", problem.Message, StringComparison.Ordinal);
        Assert.Equal("ccc", Assert.IsType<DiagramProblemElementLocation>(problem.Location).Id);
    }

    [Fact]
    public void MixedPrecisionWithinOneElement_IsReported()
    {
        // Arrange & act & assert.
        Assert.Single(Of(
            "timeline: 1\nelements:\n  - id: aaa\n    begin: 2026-01-01\n    end: 2026-01-05T12:00:00\n    row: 0\n",
            TimelineRules.MixedPrecision));
    }

    [Fact]
    public void ADuplicateId_IsReportedOnTheSecondDeclaration()
    {
        // Arrange & act & assert.
        Assert.Single(Of("""
            timeline: 1
            elements:
              - id: aaa
                begin: 2026-01-01
                row: 0
              - id: aaa
                begin: 2026-02-01
                row: 1
            """, TimelineRules.DuplicateId));
    }

    [Fact]
    public void AMissingId_IsReportedWithALine()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            timeline: 1
            elements:
              - label: Anonymous
                begin: 2026-01-01
                row: 0
            """, TimelineRules.MissingId));

        // Assert.
        // No id means no element location, so the line is what the user gets.
        Assert.Equal(3u, Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number);
    }
}
