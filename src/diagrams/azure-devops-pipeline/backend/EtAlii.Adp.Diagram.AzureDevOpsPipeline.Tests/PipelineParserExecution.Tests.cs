using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The properties that decide whether an element runs, and where. These are what the property grid
/// shows and what the canvas draws its indicators from, so reading one wrongly means a diagram that
/// states something untrue about the pipeline rather than one that merely looks wrong.
/// </summary>
public class PipelineParserExecutionTests
{
    private static PipelineModel ParseFixture(string name) =>
        PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));

    private static PipelineModel Parse(string text) => PipelineParser.Parse(LineDocument.Parse(text));

    [Fact]
    public void APipelineLevelPool_ReachesAJobThatDeclaresNone()
    {
        // Arrange & act.
        var model = ParseFixture("jobs-only.yml");

        // Assert.
        var build = model.Jobs.Single(job => job.Name == "Build");
        Assert.Equal("ubuntu-latest", build.Pool.VmImage);
        Assert.Equal(PipelinePoolOrigin.Pipeline, build.Pool.Origin);
        Assert.True(build.Pool.IsInheritedByAJob);
    }

    [Fact]
    public void AStagesPool_BeatsThePipelines()
    {
        // Arrange.
        const string text = """
            pool:
              vmImage: ubuntu-latest

            stages:
              - stage: Build
                pool:
                  vmImage: windows-latest
                jobs:
                  - job: Compile
                    steps:
                      - script: echo build
            """;

        // Act.
        var model = Parse(text);

        // Assert.
        var stage = Assert.Single(model.Stages);
        Assert.Equal("windows-latest", stage.Pool.VmImage);
        Assert.Equal(PipelinePoolOrigin.Stage, stage.Pool.Origin);
        Assert.Equal("windows-latest", stage.Jobs[0].Pool.VmImage);
        Assert.Equal(PipelinePoolOrigin.Stage, stage.Jobs[0].Pool.Origin);
    }

    [Fact]
    public void AJobsOwnPool_BeatsBoth()
    {
        // Arrange: the full three-level chain, which Requirement 4.7 spells out in that order.
        const string text = """
            pool:
              vmImage: ubuntu-latest

            stages:
              - stage: Build
                pool:
                  vmImage: windows-latest
                jobs:
                  - job: Compile
                    pool:
                      vmImage: macos-latest
                    steps:
                      - script: echo build
            """;

        // Act.
        var model = Parse(text);

        // Assert.
        var job = model.Jobs.Single();
        Assert.Equal("macos-latest", job.Pool.VmImage);
        Assert.Equal(PipelinePoolOrigin.Job, job.Pool.Origin);
        Assert.False(job.Pool.IsInheritedByAJob);
    }

    [Fact]
    public void APipelineWithNoPoolAnywhere_SaysSoRatherThanInventingOne()
    {
        // Arrange & act.
        var model = Parse("stages:\n  - stage: Build\n    jobs:\n      - job: Compile\n        steps:\n          - script: x\n");

        // Assert.
        var job = model.Jobs.Single();
        Assert.False(job.Pool.IsDeclared);
        Assert.Equal(PipelinePoolOrigin.None, job.Pool.Origin);
    }

    [Fact]
    public void ABarePoolName_ReadsAsAName()
    {
        // Arrange: `pool: Default` names a self-hosted agent pool; the mapping form is the other
        // shape the schema allows, and both have to land in the same place.
        var model = Parse("pool: Default\njobs:\n  - job: A\n    steps:\n      - script: x\n");

        // Act.
        var pool = model.Jobs.Single().Pool;

        // Assert.
        Assert.Equal("Default", pool.Name);
        Assert.Equal("", pool.VmImage);
        Assert.Equal("Default", pool.Label);
    }

    [Fact]
    public void APoolsDemands_AreCarried()
    {
        // Arrange & act.
        var model = Parse(
            "pool:\n  name: Default\n  demands:\n    - agent.os -equals Linux\n    - docker\njobs:\n  - job: A\n    steps:\n      - script: x\n");

        // Assert.
        Assert.Equal(["agent.os -equals Linux", "docker"], model.Jobs.Single().Pool.Demands);
    }

    [Fact]
    public void AMergedInPool_IsInherited()
    {
        // Arrange: edge-anchors.yml shares a pool between two stages through a YAML merge key,
        // which only works if the merge is expanded before the pool is looked for.
        var model = ParseFixture("edge-anchors.yml");

        // Act.
        var one = model.Stages.Single(stage => stage.Name == "One");

        // Assert.
        Assert.Equal("ubuntu-latest", one.Pool.VmImage);
        Assert.Equal(PipelinePoolOrigin.Stage, one.Pool.Origin);
    }

    [Fact]
    public void AStagesCondition_IsCarriedVerbatim()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var staging = model.Stages.Single(stage => stage.Name == "DeployStaging");
        Assert.Equal(
            "and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))",
            staging.Execution.Condition);
        Assert.True(staging.Execution.HasCondition);
    }

    [Fact]
    public void AnExpressionValuedCondition_SurvivesUnevaluated()
    {
        // Arrange: a runtime expression is not knowable here, and the only honest thing to do
        // with it is carry it through untouched.
        var model = ParseFixture("edge-expressions.yml");

        // Act.
        var report = model.Stages.Single(stage => stage.Name == "Report");

        // Assert.
        Assert.Equal(
            "eq(dependencies.Build.outputs['Compile.result'], 'Succeeded')",
            report.Execution.Condition);
    }

    [Fact]
    public void AJobsTimeoutAndContinueOnError_AreCarried()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var integration = model.Jobs.Single(job => job.Name == "Integration");
        Assert.Equal("30", integration.Execution.TimeoutInMinutes);
        Assert.Equal("true", integration.Execution.ContinueOnError);
        Assert.True(integration.Execution.ContinuesOnError);
    }

    [Fact]
    public void AStageWithNoneOfThese_CarriesTheDefault()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var build = model.Stages.Single(stage => stage.Name == "Build");
        Assert.Same(PipelineExecution.Default, build.Execution);
        Assert.False(build.Execution.HasCondition);
    }

    [Fact]
    public void ADisabledStep_IsKnownToBeDisabled()
    {
        // Arrange & act.
        var model = Parse("steps:\n  - script: echo x\n    enabled: false\n");

        // Assert.
        Assert.True(model.Steps.Single().Execution.IsDisabled);
    }

    [Fact]
    public void AnExpressionValuedEnabled_IsNotTreatedAsDisabled()
    {
        // Arrange: guessing here would draw a step as skipped that in fact runs, which is worse
        // than showing the expression and letting the reader decide.
        var model = Parse("steps:\n  - script: echo x\n    enabled: $(runIt)\n");

        // Act.
        var step = model.Steps.Single();

        // Assert.
        Assert.Equal("$(runIt)", step.Execution.Enabled);
        Assert.False(step.Execution.IsDisabled);
    }

    [Fact]
    public void AManuallyTriggeredStage_IsKnownToWaitForAPerson()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        Assert.True(model.Stages.Single(stage => stage.Name == "DeployProduction").TriggerIsManual);
        Assert.False(model.Stages.Single(stage => stage.Name == "Build").TriggerIsManual);
    }

    [Fact]
    public void ARepositoryTrigger_IsNotAManualStageTrigger()
    {
        // Arrange: `trigger` at the top of a file lists branches. Only `trigger: manual` on a
        // stage means "wait to be started", and the two must not be confused.
        var model = Parse("trigger:\n  - main\nstages:\n  - stage: Build\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Assert.
        Assert.False(model.Stages.Single().TriggerIsManual);
    }

    [Fact]
    public void AnUnskippableStage_CarriesItsIsSkippable()
    {
        // Arrange & act.
        var model = Parse("stages:\n  - stage: Audit\n    isSkippable: false\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Assert.
        Assert.Equal("false", model.Stages.Single().IsSkippable);
    }

    [Fact]
    public void AMatrixStrategy_MultipliesTheJobByItsEntries()
    {
        // Arrange: Requirement 4.6 - a job that becomes several at run time must not look like one.
        const string text = """
            jobs:
              - job: Test
                strategy:
                  matrix:
                    linux:
                      image: ubuntu-latest
                    windows:
                      image: windows-latest
                    mac:
                      image: macos-latest
                steps:
                  - script: dotnet test
            """;

        // Act.
        var job = Parse(text).Jobs.Single();

        // Assert.
        Assert.Equal(PipelineStrategyKind.Matrix, job.Strategy.Kind);
        Assert.Equal(3, job.Strategy.Multiplicity);
        Assert.True(job.Strategy.IsMultiplied);
        Assert.Equal("", job.Strategy.MultiplicityExpression);
    }

    [Fact]
    public void AParallelStrategy_MultipliesTheJobByItsCount()
    {
        // Arrange & act.
        var job = Parse("jobs:\n  - job: Test\n    strategy:\n      parallel: 5\n    steps:\n      - script: x\n").Jobs.Single();

        // Assert.
        Assert.Equal(PipelineStrategyKind.Parallel, job.Strategy.Kind);
        Assert.Equal(5, job.Strategy.Multiplicity);
    }

    [Fact]
    public void AParallelCountGivenAsAVariable_AdmitsItIsNotKnownYet()
    {
        // Arrange: pretending to know a number nobody knows until the run starts would put a
        // wrong count on the canvas, which is worse than saying it depends.
        var job = Parse("jobs:\n  - job: Test\n    strategy:\n      parallel: $(slices)\n    steps:\n      - script: x\n").Jobs.Single();

        // Assert.
        Assert.Equal(1, job.Strategy.Multiplicity);
        Assert.Equal("$(slices)", job.Strategy.MultiplicityExpression);
        Assert.True(job.Strategy.IsMultiplied);
    }

    [Fact]
    public void ADeploymentStrategy_MultipliesNothing()
    {
        // Arrange: runOnce decides which hooks run, not how many jobs there are.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var staging = model.Jobs.Single(job => job.Name == "Staging");

        // Assert.
        Assert.Equal(1, staging.Strategy.Multiplicity);
        Assert.False(staging.Strategy.IsMultiplied);
    }

    [Fact]
    public void AJobWithNoStrategy_RunsOnce()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var compile = model.Jobs.Single(job => job.Name == "Compile");
        Assert.Equal(PipelineStrategyKind.None, compile.Strategy.Kind);
        Assert.Equal(1, compile.Strategy.Multiplicity);
        Assert.False(compile.Strategy.IsMultiplied);
    }
}
