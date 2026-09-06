using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The mistakes a YAML file hides, which is most of what anybody would draw this diagram for.
/// </summary>
/// <remarks>
/// Every one of these runs from a plain YAML string with no file, no canvas and no connection
/// (Requirement 10.9) - which is what makes a rule cheap enough to write a test per case, and is
/// the shape C4RuleSet established.
/// </remarks>
public class PipelineRuleSetTests
{
    private static IReadOnlyList<DiagramProblem> Judge(string yaml) =>
        PipelineRuleSet.Judge(PipelineParser.Parse(PipelineDocument.Parse(yaml)));

    private static IEnumerable<DiagramProblem> Of(string yaml, string ruleId) =>
        Judge(yaml).Where(problem => problem.RuleId == ruleId);

    private const string Healthy = """
        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: dotnet build
          - stage: Test
            dependsOn: Build
            jobs:
              - job: Verify
                steps:
                  - script: dotnet test
        """;

    [Fact]
    public void ACorrectPipeline_HasNothingWrongWithIt()
    {
        // Arrange & act & assert.
        // The test that matters most: a rule that fires on a healthy file is worse than no rule,
        // because it teaches the reader to ignore the panel.
        Assert.Empty(Judge(Healthy));
    }

    [Fact]
    public void EveryFixtureThatIsMeantToBeValid_ReportsNothing()
    {
        // Arrange: the corpus is real pipelines. Only edge-broken-graph.yml is deliberately wrong.
        var fixtures = Directory.GetFiles(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures"), "*.yml", SearchOption.AllDirectories);

        // Assert, first, that the walk found the corpus at all: 14 fixtures ship today, so
        // ten is a floor with headroom. Without this the test passes loudest exactly when it
        // has stopped looking at anything - and it used to enumerate a relative "Fixtures",
        // which resolves against the working directory rather than the test binary.
        Assert.True(
            fixtures.Length >= 10,
            $"Only {fixtures.Length} pipeline fixtures were found; this guard has stopped finding the corpus it judges.");

        foreach (var path in fixtures)
        {
            if (IoPath.GetFileName(path) == "edge-broken-graph.yml")
            {
                continue;
            }

            // Act.
            var problems = Judge(File.ReadAllText(path));

            // Assert.
            var errors = problems.Where(problem => problem.Severity == DiagramProblemSeverity.Error).ToList();
            Assert.True(errors.Count == 0, $"{IoPath.GetFileName(path)}: {string.Join("; ", errors.Select(problem => problem.Message))}");
        }
    }

    [Fact]
    public void ADependsOnNamingNothing_IsReportedWithBothNames()
    {
        // Arrange: Requirement 10.2 - the commonest real mistake, and the one a file is worst at
        // showing, since the name sits plainly a few lines from what it fails to match.
        var problem = Assert.Single(Of("""
            stages:
              - stage: Build
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Ship
                dependsOn: Bild
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.DanglingDependency));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("Ship", problem.Message);
        Assert.Contains("Bild", problem.Message);
    }

    [Fact]
    public void ADanglingJobDependency_IsReportedToo()
    {
        // Arrange: the rules run at both levels, and a job's dependsOn is matched within its stage.
        var problem = Assert.Single(Of("""
            jobs:
              - job: Build
                steps:
                  - script: x
              - job: Test
                dependsOn: Compile
                steps:
                  - script: x
            """, PipelineRules.DanglingDependency));

        // Assert.
        Assert.Contains("job called 'Compile'", problem.Message);
    }

    [Fact]
    public void ACycle_IsReportedNamingTheWholeLoop()
    {
        // Arrange: Requirement 10.3 - "Left waits for Right waits for Left" is actionable, and
        // "this pipeline has a cycle" is not.
        var problem = Assert.Single(Of("""
            stages:
              - stage: Left
                dependsOn: Right
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Right
                dependsOn: Left
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.Cycle));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("Left", problem.Message);
        Assert.Contains("Right", problem.Message);
    }

    [Fact]
    public void AStageWaitingForItself_SaysExactlyThat()
    {
        // Arrange: the degenerate cycle, which reads badly as a list of one.
        var problem = Assert.Single(Of("""
            stages:
              - stage: Loop
                dependsOn: Loop
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.Cycle));

        // Assert.
        Assert.Contains("waits for itself", problem.Message);
    }

    [Fact]
    public void APipelineThatCannotStart_IsReported()
    {
        // Arrange: Requirement 10.5 - every stage waiting for another one means there is no entry
        // point, which is invisible while reading the file and obvious once it is drawn.
        var problem = Assert.Single(Of("""
            stages:
              - stage: A
                dependsOn: B
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: B
                dependsOn: A
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.NoStartingStage));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Null(problem.Location);
    }

    [Fact]
    public void AnOrdinaryPipeline_IsNotAccusedOfHavingNoStart()
    {
        // Arrange & act & assert.
        Assert.Empty(Of(Healthy, PipelineRules.NoStartingStage));
    }

    [Fact]
    public void AStageNothingCanReach_IsReported()
    {
        // Arrange: Requirement 10.4 - an orphaned stage silently never runs, which is precisely
        // the failure a reader opened the diagram to find.
        var problem = Assert.Single(Of("""
            stages:
              - stage: Build
                dependsOn: []
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Ghost
                dependsOn: Missing
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: After
                dependsOn: Ghost
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.Unreachable));

        // Assert.
        // Ghost's own problem is the dangling name, which is the one that names the fix. After is
        // the one reported here: its cause is genuinely upstream, and nothing else would say so.
        Assert.Contains("After", problem.Message);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
    }

    [Fact]
    public void AStageWithADanglingDependency_IsNotAlsoReportedAsUnreachable()
    {
        // Arrange: one mistake, one problem. "Ship waits for a stage called Bild, which this
        // pipeline does not have" names the fix; "and therefore nothing can reach Ship" is a
        // consequence of it, and a panel that says both teaches the reader that it repeats itself.
        var yaml = """
            stages:
              - stage: Build
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Ship
                dependsOn: Bild
                jobs:
                  - job: J
                    steps:
                      - script: x
            """;

        // Act & assert.
        Assert.Single(Of(yaml, PipelineRules.DanglingDependency));
        Assert.Empty(Of(yaml, PipelineRules.Unreachable));
    }

    [Fact]
    public void TheLastStageOfAPipeline_IsNotCalledUnreachable()
    {
        // Arrange: nothing depends on the last stage of every correct pipeline, so a rule that
        // only asked "does anything depend on this" would fire on every file in the world.
        Assert.Empty(Of(Healthy, PipelineRules.Unreachable));
    }

    [Fact]
    public void AStageInACycle_IsNotAlsoReportedAsUnreachable()
    {
        // Arrange: true, and unhelpful - fixing the cycle fixes this too, and two problems on one
        // mistake teaches the reader that the panel repeats itself.
        var yaml = """
            stages:
              - stage: Start
                dependsOn: []
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Left
                dependsOn: Right
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Right
                dependsOn: Left
                jobs:
                  - job: J
                    steps:
                      - script: x
            """;

        // Act & assert.
        Assert.Single(Of(yaml, PipelineRules.Cycle));
        Assert.Empty(Of(yaml, PipelineRules.Unreachable));
    }

    [Fact]
    public void AnUnnamedStage_IsReportedGently()
    {
        // Arrange: Requirement 10.6 - the pipeline still runs, but nothing can depend on it and
        // nobody can find it in a log. The cost arrives later, which is when a warning earns its
        // place.
        var problem = Assert.Single(Of("""
            stages:
              - stage:
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.Unnamed));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("no name", problem.Message);
    }

    [Fact]
    public void AStageWithOnlyADisplayName_IsNotReported()
    {
        // Arrange: a display name is something to call it by, which is what the rule is about.
        Assert.Empty(Of("""
            stages:
              - stage:
                displayName: The one at the start
                jobs:
                  - job: J
                    steps:
                      - script: x
            """, PipelineRules.Unnamed));
    }

    [Fact]
    public void TheImplicitStage_IsNotReportedAsUnnamed()
    {
        // Arrange: the schema conjured it around a bare jobs list. It has no name because it is
        // not in the file, and telling the user to name it would be advice they cannot take.
        Assert.Empty(Of("jobs:\n  - job: Build\n    steps:\n      - script: x\n", PipelineRules.Unnamed));
    }

    [Fact]
    public void ATemplateInAnotherRepository_IsAWarningRatherThanAnError()
    {
        // Arrange: Requirement 10.7 - an unfollowable template is a limit of the reader, not a
        // defect in the pipeline. The severity enum has no informational level, and Warning's own
        // definition - worth attention, the document still means something - is what 10.7 wants.
        var problem = Assert.Single(Of("""
            stages:
              - stage: Build
                jobs:
                  - job: J
                    steps:
                      - script: x
              - template: deploy.yml@shared
            """, PipelineRules.TemplateNotFollowed));

        // Assert.
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Contains("another repository", problem.Message);
    }

    [Fact]
    public void ATemplatePathBuiltFromAnExpression_IsReportedToo()
    {
        // Arrange & act.
        var problem = Assert.Single(Of("""
            stages:
              - stage: Build
                jobs:
                  - job: J
                    steps:
                      - script: x
              - template: templates/${{ parameters.which }}.yml
            """, PipelineRules.TemplateNotFollowed));

        // Assert.
        Assert.Contains("compile time", problem.Message);
    }

    [Fact]
    public void AnOrdinaryRelativeTemplate_IsNotReported()
    {
        // Arrange: whether it resolves depends on the filesystem, which a pure function does not
        // have - so it is not guessed at.
        Assert.Empty(Of("""
            stages:
              - stage: Build
                jobs:
                  - template: templates/build-jobs.yml
            """, PipelineRules.TemplateNotFollowed));
    }

    [Fact]
    public void EveryProblemNamesTheRuleThatFoundIt()
    {
        // Arrange: a rule id is what a user filters on and what a log names, so a problem without
        // one cannot be acted on twice.
        var problems = Judge(File.ReadAllText(IoPath.Combine("Fixtures", "edge-broken-graph.yml")));

        // Assert.
        Assert.NotEmpty(problems);
        Assert.All(problems, problem =>
        {
            ArgumentNullException.ThrowIfNull(problem);

            Assert.StartsWith("azure-pipeline.", problem.RuleId, StringComparison.Ordinal);
            Assert.NotEmpty(problem.Message);
        });
    }

    [Fact]
    public void AProblemOnAnElement_NamesTheElementSoTheCanvasCanMarkIt()
    {
        // Arrange: Requirement 8.7 - a dangling dependsOn should be visible where it is rather
        // than only in a list. That needs the element id, which is what the delta stream carries.
        var problems = Judge(File.ReadAllText(IoPath.Combine("Fixtures", "edge-broken-graph.yml")));

        // Act.
        var dangling = problems.Single(problem => problem.RuleId == PipelineRules.DanglingDependency);

        // Assert.
        var location = Assert.IsType<DiagramProblemElementLocation>(dangling.Location);
        Assert.Equal("Ghost", location.Id);
    }

    [Fact]
    public void TheDeliberatelyBrokenFixture_ReportsEverythingItWasWrittenToShow()
    {
        // Arrange: the fixture's own comment says it carries a dangling dependsOn, a two-stage
        // cycle and a stage nothing can reach.
        var problems = Judge(File.ReadAllText(IoPath.Combine("Fixtures", "edge-broken-graph.yml")));

        // Assert.
        Assert.Contains(problems, problem => problem.RuleId == PipelineRules.DanglingDependency);
        Assert.Contains(problems, problem => problem.RuleId == PipelineRules.Cycle);
    }

    [Fact]
    public void AFileThatDeclaresNothing_IsNotAccusedOfAnything()
    {
        // Arrange: an `extends` file's shape is the template's, and a new pipeline is routinely
        // half-written. A rule firing on either would fire on every new file.
        Assert.Empty(Judge("trigger:\n  - main\n"));
        Assert.Empty(Judge(File.ReadAllText(IoPath.Combine("Fixtures", "extends.yml"))));
    }
}
