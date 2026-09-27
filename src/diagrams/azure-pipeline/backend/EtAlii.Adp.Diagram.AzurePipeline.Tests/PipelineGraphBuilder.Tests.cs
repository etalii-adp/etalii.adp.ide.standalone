using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The dependency rules, which are the reason this diagram is worth having - and which are
/// opposite at the two levels they apply to. The same two lines of YAML mean "run these one after
/// another" under <c>stages</c> and "run these all at once" under <c>jobs</c>, so most of what is
/// tested here is that the defaults are not quietly shared.
/// </summary>
public class PipelineGraphBuilderTests
{
    private static PipelineModel ParseFixture(string name) =>
        PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));

    private static PipelineModel Parse(string text) => PipelineParser.Parse(LineDocument.Parse(text));

    [Fact]
    public void AStageWithNoDependsOn_WaitsForTheStageBeforeIt()
    {
        // Arrange: the single most easily missed thing about an Azure pipeline - the ordering is
        // in the file without being written down.
        var model = Parse("""
            stages:
              - stage: One
                jobs:
                  - job: A
                    steps:
                      - script: x
              - stage: Two
                jobs:
                  - job: B
                    steps:
                      - script: x
            """);

        // Act.
        var graph = PipelineGraphBuilder.OfStages(model);

        // Assert.
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("One", edge.FromId);
        Assert.Equal("Two", edge.ToId);
        Assert.True(edge.IsImplicit);
    }

    [Fact]
    public void TheFirstStage_WaitsForNothing()
    {
        // Arrange & act.
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("multi-stage.yml"));

        // Assert.
        Assert.Empty(graph.DependenciesOf("Build"));
    }

    [Fact]
    public void AStageWithAnEmptyDependsOn_WaitsForNothing()
    {
        // Arrange: `dependsOn: []` is a real instruction, and the opposite of saying nothing.
        var model = Parse("""
            stages:
              - stage: One
                jobs:
                  - job: A
                    steps:
                      - script: x
              - stage: Two
                dependsOn: []
                jobs:
                  - job: B
                    steps:
                      - script: x
            """);

        // Act.
        var graph = PipelineGraphBuilder.OfStages(model);

        // Assert.
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void JobsWithNoDependsOn_RunInParallelRatherThanInOrder()
    {
        // Arrange: the opposite default, and the one that catches people out. Unit and Integration
        // in multi-stage.yml carry a comment saying exactly this.
        var stage = ParseFixture("multi-stage.yml").Stages.Single(candidate => candidate.Name == "Test");

        // Act.
        var graph = PipelineGraphBuilder.OfJobs(stage);

        // Assert.
        Assert.Equal(["Test/Unit", "Test/Integration"], graph.NodeIds);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void AJobWithADependsOn_WaitsForTheJobItNames()
    {
        // Arrange & act.
        var stage = ParseFixture("jobs-only.yml").Stages.Single();
        var graph = PipelineGraphBuilder.OfJobs(stage);

        // Assert.
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("stage-0/Build", edge.FromId);
        Assert.Equal("stage-0/Test", edge.ToId);
        Assert.False(edge.IsImplicit);
    }

    [Fact]
    public void FanOut_IsRepresented()
    {
        // Arrange: one stage feeding several, which is half of the shape the ordering exists to
        // express (Requirement 6.3).
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("multi-stage.yml"));

        // Act.
        var fromTest = graph.DependentsOf("Test").ToList();

        // Assert.
        Assert.Equal(["DeployStaging", "DeployProduction"], fromTest);
    }

    [Fact]
    public void FanIn_IsRepresented()
    {
        // Arrange: and the other half - several converging on one.
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("multi-stage.yml"));

        // Act.
        var intoNotify = graph.DependenciesOf("Notify").ToList();

        // Assert.
        Assert.Equal(["DeployStaging", "DeployProduction"], intoNotify);
    }

    [Fact]
    public void ADependsOnNamingNothing_KeepsItsEdgeAndMarksItBroken()
    {
        // Arrange: a dangling dependency is precisely the mistake this diagram should catch, so
        // dropping the edge would hide the thing the reader opened it to find (Requirement 6.5).
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("edge-broken-graph.yml"));

        // Act.
        var broken = Assert.Single(graph.BrokenEdges);

        // Assert.
        Assert.Equal("Ghost", broken.ToId);
        Assert.Equal("DoesNotExist", broken.FromName);
        Assert.Equal("", broken.FromId);
    }

    [Fact]
    public void ACycle_IsReportedNamingTheElementsInIt()
    {
        // Arrange: "Left waits for Right waits for Left" is actionable; "this pipeline has a
        // cycle" is not (Requirement 6.6).
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("edge-broken-graph.yml"));

        // Act.
        var cycle = Assert.Single(graph.Cycles);

        // Assert.
        Assert.Equal(["Left", "Right"], cycle.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ACycle_DoesNotStopTheRestOfTheGraphBeingBuilt()
    {
        // Arrange & act.
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("edge-broken-graph.yml"));

        // Assert.
        Assert.Equal(["Build", "Ghost", "Left", "Right"], graph.NodeIds);
        Assert.NotEmpty(graph.Edges);
    }

    [Fact]
    public void AStageDependingOnItself_IsACycleOfOne()
    {
        // Arrange: the degenerate case, which a walk that only looked for pairs would miss.
        var model = Parse("stages:\n  - stage: Loop\n    dependsOn: Loop\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Act.
        var graph = PipelineGraphBuilder.OfStages(model);

        // Assert.
        Assert.Equal(["Loop"], Assert.Single(graph.Cycles));
    }

    [Fact]
    public void ALongerCycle_IsReportedOnceRatherThanOncePerWayIn()
    {
        // Arrange: the same loop is reachable from every node on it.
        var model = Parse("""
            stages:
              - stage: A
                dependsOn: C
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
              - stage: C
                dependsOn: B
                jobs:
                  - job: J
                    steps:
                      - script: x
            """);

        // Act.
        var graph = PipelineGraphBuilder.OfStages(model);

        // Assert.
        var cycle = Assert.Single(graph.Cycles);
        Assert.Equal(["A", "B", "C"], cycle.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnAcyclicPipeline_ReportsNoCycles()
    {
        // Arrange & act.
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("multi-stage.yml"));

        // Assert.
        Assert.Empty(graph.Cycles);
    }

    [Theory]
    [InlineData("", PipelineEdgeCondition.OnSuccess)]
    [InlineData("succeeded()", PipelineEdgeCondition.OnSuccess)]
    [InlineData("succeeded('Build')", PipelineEdgeCondition.OnSuccess)]
    [InlineData("failed()", PipelineEdgeCondition.OnFailure)]
    [InlineData("always()", PipelineEdgeCondition.Always)]
    [InlineData("succeededOrFailed()", PipelineEdgeCondition.OnSuccessOrFailure)]
    [InlineData("SucceededOrFailed()", PipelineEdgeCondition.OnSuccessOrFailure)]
    public void AWellKnownCondition_SaysWhichOutcomeItWaitsFor(string condition, PipelineEdgeCondition expected)
    {
        // Act & assert.
        // An edge that only fires on failure is a different arrow from one that fires on success.
        Assert.Equal(expected, PipelineGraphBuilder.Classify(condition));
    }

    [Theory]
    [InlineData("and(succeeded(), eq(variables['x'], 'y'))")]
    [InlineData("eq(dependencies.Build.outputs['Compile.result'], 'Succeeded')")]
    [InlineData("${{ parameters.deploy }}")]
    public void AConditionThisModuleWillNotInterpret_IsCustomRatherThanGuessedAt(string condition)
    {
        // Act & assert.
        // `and(succeeded(), ...)` does wait for success, but it waits for something else as well -
        // drawing it as a plain success edge would tell the reader the second half is not there.
        Assert.Equal(PipelineEdgeCondition.Custom, PipelineGraphBuilder.Classify(condition));
    }

    [Fact]
    public void ATrueDifferenceBetweenAlwaysAndSucceededOrFailed_IsKept()
    {
        // Arrange: succeededOrFailed does not run on a cancelled build and always does, which is
        // the entire reason both exist.
        Assert.NotEqual(
            PipelineGraphBuilder.Classify("always()"),
            PipelineGraphBuilder.Classify("succeededOrFailed()"));
    }

    [Fact]
    public void AnEdgeIntoAConditionalStage_CarriesTheConditionVerbatim()
    {
        // Arrange & act.
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("multi-stage.yml"));

        // Assert.
        // A condition is on the element, so it marks every edge into it - Notify waits for both
        // deployments and runs whatever either of them did.
        var edges = graph.Edges.Where(candidate => candidate.ToId == "Notify").ToList();
        Assert.Equal(2, edges.Count);
        Assert.All(edges, edge =>
        {
            ArgumentNullException.ThrowIfNull(edge);

            Assert.True(edge.IsConditional);
            Assert.Equal("always()", edge.ConditionText);
            Assert.Equal(PipelineEdgeCondition.Always, edge.Condition);
        });
    }

    [Fact]
    public void AnEdgeIntoAnUnconditionalStage_IsNotMarkedConditional()
    {
        // Arrange & act.
        var graph = PipelineGraphBuilder.OfStages(ParseFixture("multi-stage.yml"));

        // Assert.
        var edge = graph.Edges.Single(candidate => candidate.ToId == "Test");
        Assert.False(edge.IsConditional);
        Assert.Equal(PipelineEdgeCondition.OnSuccess, edge.Condition);
    }

    [Fact]
    public void ADependsOnMatchesByName_NotById()
    {
        // Arrange: a job's id carries its stage, so that two stages may each have a job called
        // Test. What dependsOn names is the name.
        var model = Parse("""
            stages:
              - stage: One
                jobs:
                  - job: Alpha
                    steps:
                      - script: x
                  - job: Beta
                    dependsOn: Alpha
                    steps:
                      - script: x
            """);

        // Act.
        var graph = PipelineGraphBuilder.OfJobs(model.Stages.Single());

        // Assert.
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("One/Alpha", edge.FromId);
        Assert.Equal("One/Beta", edge.ToId);
    }

    [Fact]
    public void AnEmptyLevel_IsAnEmptyGraph()
    {
        // Arrange: a stage with no jobs is a real thing to draw, not a reason to fail.
        var model = Parse("stages:\n  - stage: Empty\n");

        // Act & assert.
        Assert.Same(PipelineGraph.Empty, PipelineGraphBuilder.OfJobs(model.Stages.Single()));
    }

    [Fact]
    public void EveryFixture_BuildsAGraphWithoutThrowing()
    {
        // Arrange: including the deliberately broken one, which is the point.
        var fixtures = Directory.GetFiles(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures"), "*.yml", SearchOption.AllDirectories);

        // Assert, first, that the walk found the corpus at all: 14 fixtures ship today, so
        // ten is a floor with headroom. Without this the test passes loudest exactly when it
        // has stopped looking at anything - and it used to enumerate a relative "Fixtures",
        // which resolves against the working directory rather than the test binary.
        Assert.True(
            fixtures.Length >= 10,
            $"Only {fixtures.Length} pipeline fixtures were found; this guard has stopped finding the corpus it builds graphs from.");

        var staged = 0;
        foreach (var path in fixtures)
        {
            var model = PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(path)));

            // Act.
            var graph = PipelineGraphBuilder.OfStages(model);

            // Assert.
            Assert.Equal(model.Stages.Count, graph.NodeIds.Count);
            foreach (var stage in model.Stages)
            {
                Assert.NotNull(PipelineGraphBuilder.OfJobs(stage));
            }

            staged += model.Stages.Count;
        }

        // And a floor on the second collection, which is the one carrying the inner assertion:
        // the equality above reads 0 == 0 on a stage-less model and the inner loop then walks
        // nothing, so a parser that stopped producing stages passes this test. Seven of the
        // fifteen fixtures declare no stage line of their own, which is why this is a floor on
        // the corpus total rather than on any single file. The corpus yields 26 stages today,
        // so fifteen is a floor with real headroom on it.
        Assert.True(
            staged >= 15,
            $"Only {staged} stages were parsed across the whole corpus; this guard has stopped finding the stages it builds job graphs from.");
    }
}
