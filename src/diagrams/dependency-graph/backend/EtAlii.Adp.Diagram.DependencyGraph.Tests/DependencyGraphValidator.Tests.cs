using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The seam itself, as distinct from the rules it carries: parse, then judge.
/// </summary>
public class DependencyGraphValidatorTests
{
    private static ValueTask<IReadOnlyList<DiagramProblem>> JudgeAsync(string document) =>
        new DependencyGraphValidator().ValidateAsync(
            new DiagramValidationRequest(document, "services", "/root", "/root/services.dgr", null),
            CancellationToken.None);

    [Fact]
    public async Task ACorrectGraph_ReachesTheRulesAndSurvivesThem()
    {
        // Arrange & act.
        var problems = await JudgeAsync("dependencies: 1\nelements:\n  - id: aaa\n    x: 0\n    row: 0\n");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AFlawedGraph_ReportsTheRuleThatFired()
    {
        // Arrange & act.
        // The wording is DependencyGraphRuleSetTests' business; this proves the document got
        // there. A relation naming a node that is not in the graph, at the offending line.
        var problems = await JudgeAsync(
            "dependencies: 1\nelements:\n  - id: aaa\n    x: 0\n    row: 0\nrelations:\n  - id: ccc\n    from: aaa\n    to: ghost\n");

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == DependencyGraphRules.DanglingRelation);
    }

    [Fact]
    public async Task AFileThatIsNotYaml_IsOneErrorNamingItsLine()
    {
        // Arrange & act.
        var problems = await JudgeAsync("elements: [\n  - id: a\n");

        // Assert.
        // One problem, not a pile of consequences from an empty model.
        var problem = Assert.Single(problems);
        Assert.Equal(DependencyGraphValidator.UnparseableRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.True(Assert.IsType<DiagramProblemLineLocation>(problem.Location).Number >= 1);
    }

    [Fact]
    public async Task AnEmptyFile_IsJudgedRatherThanCrashed()
    {
        // Arrange & act.
        var problems = await JudgeAsync("");

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == DependencyGraphValidator.UnparseableRuleId);
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        Assert.Equal(Diagram.DependencyGraph.Origin, new DependencyGraphValidator().Origin);
    }
}
