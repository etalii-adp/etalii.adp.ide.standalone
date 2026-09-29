using Xunit;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The seam itself, as distinct from the rules it carries: parse, then judge (Requirement 10.1).
/// </summary>
/// <remarks>
/// <see cref="PipelineRuleSetTests"/> covers what each rule says, over a model. What is left here
/// is the join - that a document really does reach those rules, that a file which will not parse
/// is reported as the one problem naming its line rather than as the pile of consequences an
/// empty model would produce, and that this answers for the right diagram type. Core resolves
/// this by <see cref="DiagramOrigin"/> alone, so a wrong origin is a validator that silently
/// never runs.
/// </remarks>
public class PipelineValidatorTests
{
    private static ValueTask<IReadOnlyList<DiagramProblem>> JudgeAsync(string document) =>
        new PipelineValidator().ValidateAsync(
            new DiagramValidationRequest(document, "azure-pipelines", "/root", "/root/azure-pipelines.yml", null),
            CancellationToken.None);

    [Fact]
    public async Task ACorrectPipeline_ReachesTheRulesAndSurvivesThem()
    {
        // Arrange & act.
        var problems = await JudgeAsync("""
            stages:
              - stage: Build
                jobs:
                  - job: Compile
                    steps:
                      - script: dotnet build
            """);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AFlawedPipeline_ReportsTheRuleThatFired()
    {
        // Arrange: a dependency on a stage that is not there. The rule's own wording is
        // PipelineRuleSetTests' business; what this proves is that the document got to it.
        var problems = await JudgeAsync("""
            stages:
              - stage: Build
                dependsOn: Missing
                jobs:
                  - job: Compile
                    steps:
                      - script: dotnet build
            """);

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == PipelineRules.DanglingDependency);
    }

    [Fact]
    public async Task AFileThatIsNotYaml_IsOneProblemNamingItsLine()
    {
        // Arrange: unterminated flow sequence - YAML the parser cannot recover from.
        var problems = await JudgeAsync("stages: [\n  - stage: Build\n");

        // Act.
        var problem = Assert.Single(problems);

        // Assert.
        // One problem, not a list of consequences: running graph rules over a model that is empty
        // only because the parse failed would bury the one thing worth reading.
        Assert.Equal(PipelineValidator.UnparseableRuleId, problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        var location = Assert.IsType<DiagramProblemLineLocation>(problem.Location);
        Assert.True(location.Number >= 1, "a line number of zero points at nothing the user can open");
    }

    [Fact]
    public async Task AnEmptyFile_IsJudgedRatherThanCrashed()
    {
        // Arrange & act.
        // An empty pipeline parses to an empty model, which the rules have something to say about;
        // what must not happen is an exception escaping into core's problem collection.
        var problems = await JudgeAsync("");

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.RuleId == PipelineValidator.UnparseableRuleId);
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        Assert.Equal(Diagram.Pipeline.Origin, new PipelineValidator().Origin);
    }
}
