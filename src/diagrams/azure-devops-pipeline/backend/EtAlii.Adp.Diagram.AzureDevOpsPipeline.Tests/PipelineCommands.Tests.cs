using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The editable set, which Requirement 9.1 calls this spec's central scope judgement: the changes
/// a diagram is good at, and none of the ones a text editor is better at. Two things run through
/// all of these - every edit reports the command that reverses it, and every edit that would
/// produce a pipeline Azure DevOps will not run is refused rather than written.
/// </summary>
public class PipelineCommandsTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-commands-" + Guid.NewGuid().ToString("N"));

    private readonly PipelineDocumentStore _store = new();

    public PipelineCommandsTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Pipeline = """
        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: dotnet build
                    displayName: Build
                  - script: dotnet pack
                    displayName: Pack
              - job: Lint
                steps:
                  - script: dotnet format

          - stage: Test
            dependsOn: Build
            jobs:
              - job: Verify
                steps:
                  - script: dotnet test
        """;

    private string Write(string content = Pipeline)
    {
        var path = IoPath.Combine(_workspace, "azure-pipelines.yml");
        File.WriteAllText(path, content);
        return path;
    }

    private PipelineModel Model(string path) => _store.GetOrLoad(_workspace, path).Model;

    private string Text(string path) => File.ReadAllText(path);

    private Task<CommandResult> Rename(string path, string elementId, string displayName) =>
        new RenamePipelineElementCommandHandler(_store).ExecuteAsync(
            new RenamePipelineElementCommand(_workspace, path, elementId, displayName));

    private Task<CommandResult> SetDependencies(string path, string elementId, string[] dependsOn, bool declared = true) =>
        new SetPipelineDependenciesCommandHandler(_store).ExecuteAsync(
            new SetPipelineDependenciesCommand(_workspace, path, elementId, dependsOn, declared));

    private Task<CommandResult> SetEnabled(string path, string elementId, bool enabled) =>
        new SetPipelineElementEnabledCommandHandler(_store).ExecuteAsync(
            new SetPipelineElementEnabledCommand(_workspace, path, elementId, enabled));

    private Task<CommandResult> Add(string path, PipelineAddKind kind, string parentId = "", string name = "") =>
        new AddPipelineElementCommandHandler(_store).ExecuteAsync(
            new AddPipelineElementCommand(_workspace, path, kind, parentId, name));

    private Task<CommandResult> Remove(string path, string elementId) =>
        new RemovePipelineElementCommandHandler(_store).ExecuteAsync(
            new RemovePipelineElementCommand(_workspace, path, elementId));

    private Task<CommandResult> MoveStep(string path, string stepId, int toIndex) =>
        new MovePipelineStepCommandHandler(_store).ExecuteAsync(
            new MovePipelineStepCommand(_workspace, path, stepId, toIndex));

    /// <summary>Runs whatever a command reported as its inverse, whichever kind it is.</summary>
    // ReSharper disable once UnusedMethodReturnValue.Local - Reason: the body is a switch expression over the inverse's kind, and a switch expression must yield a value, so it returns each handler's CommandResult; the callers await it only for its effect on the store.
    private async Task<CommandResult> UndoAsync(ICommand inverse) => inverse switch
    {
        RenamePipelineElementCommand rename => await new RenamePipelineElementCommandHandler(_store).ExecuteAsync(rename),
        SetPipelineDependenciesCommand set => await new SetPipelineDependenciesCommandHandler(_store).ExecuteAsync(set),
        SetPipelineElementEnabledCommand enabled => await new SetPipelineElementEnabledCommandHandler(_store).ExecuteAsync(enabled),
        RemovePipelineElementCommand remove => await new RemovePipelineElementCommandHandler(_store).ExecuteAsync(remove),
        RestorePipelineLinesCommand restore => await new RestorePipelineLinesCommandHandler(_store).ExecuteAsync(restore),
        MovePipelineStepCommand move => await new MovePipelineStepCommandHandler(_store).ExecuteAsync(move),
        _ => throw new InvalidOperationException($"No handler for {inverse.GetType().Name} in this test."),
    };

    [Fact]
    public async Task ARename_SetsTheDisplayNameAndIsUndoable()
    {
        // Arrange.
        var path = Write();
        var before = Text(path);

        // Act.
        var result = await Rename(path, "Build", "Build the solution");

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Contains("displayName: Build the solution", Text(path));

        // Act: and back again.
        await UndoAsync(result.Inverse!);

        // Assert.
        // Byte for byte, not merely equivalent - the undo of an edit to a build definition has to
        // leave the file the reviewer already approved.
        Assert.Equal(before, Text(path));
    }

    [Fact]
    public async Task ARenameOfSomethingGone_SaysSoRatherThanThrowing()
    {
        // Arrange: handlers are re-run by undo and redo, so each validates its own preconditions.
        var path = Write();

        // Act.
        var result = await Rename(path, "NoSuchStage", "Anything");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no longer in this pipeline", result.Error);
    }

    [Fact]
    public async Task ARenameThatChangesNothing_IsNotRecordedAsUndoable()
    {
        // Arrange: an undo entry for an edit that did not happen is an undo that appears to do
        // nothing when the user reaches it.
        var path = Write();

        // Act.
        var result = await Rename(path, "Build", "");

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Null(result.Inverse);
    }

    /// <summary>
    /// Three stages relying entirely on the sequential default, which is where "making the
    /// implicit explicit" has something to do.
    /// </summary>
    private const string Sequential = """
        stages:
          - stage: A
            jobs:
              - job: J
                steps:
                  - script: x
          - stage: B
            jobs:
              - job: J
                steps:
                  - script: x
          - stage: C
            jobs:
              - job: J
                steps:
                  - script: x
        """;

    [Fact]
    public async Task DrawingAnEdge_WritesTheDependency()
    {
        // Arrange: C waits for B today, only because nothing says otherwise.
        var path = Write(Sequential);

        // Act.
        var result = await SetDependencies(path, "C", ["A"]);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["A"], Model(path).Stages.Single(stage => stage.Name == "C").DependsOn);
    }

    [Fact]
    public async Task DrawingAnEdgeThatClosesALoop_IsRefused()
    {
        // Arrange: the graph reports a cycle it finds in somebody else's file, because hiding it
        // would be worse - but a cycle this diagram is about to write it simply does not write.
        var path = Write();

        // Act.
        var result = await SetDependencies(path, "Build", ["Test"]);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already waiting for it", result.Error);
        Assert.DoesNotContain("dependsOn: Test", Text(path));
    }

    [Fact]
    public async Task RemovingAnEdge_PutsTheElementBackOnTheDefault()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await SetDependencies(path, "Test", [], declared: false);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.False(Model(path).Stages.Single(stage => stage.Name == "Test").DependsOnDeclared);
    }

    [Fact]
    public async Task MakingTheImplicitExplicit_WritesADependsOnThatWasNotThere()
    {
        // Arrange: Requirement 9.3. None of these stages has a dependsOn - the ordering is in the
        // file without being written down. Drawing an edge that contradicts it is the moment the
        // default has to become explicit, or the file would say something the diagram does not.
        var path = Write(Sequential);
        Assert.False(Model(path).Stages.Single(stage => stage.Name == "C").DependsOnDeclared);

        // Act.
        var result = await SetDependencies(path, "C", ["A"]);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("dependsOn: A", Text(path));
        Assert.True(Model(path).Stages.Single(stage => stage.Name == "C").DependsOnDeclared);
        // B is untouched and still relies on the default, which is the point: only the element
        // whose ordering actually changed had to be written down.
        Assert.False(Model(path).Stages.Single(stage => stage.Name == "B").DependsOnDeclared);
    }

    [Fact]
    public async Task ADependencyOnSomethingThatDoesNotExist_IsRefused()
    {
        // Arrange: a dangling dependsOn is a mistake this diagram exists to catch, so it must
        // certainly not be a mistake this diagram makes.
        var path = Write();

        // Act.
        var result = await SetDependencies(path, "Test", ["Imaginary"]);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("nothing called 'Imaginary'", result.Error);
    }

    [Fact]
    public async Task TakingTheLastStartingStagesFreedom_IsRefused()
    {
        // Arrange: Requirement 9.4 - a pipeline must contain at least one stage with no
        // dependencies, or there is nothing for it to begin with.
        var path = Write();

        // Act.
        var result = await SetDependencies(path, "Build", ["Build"]);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task AStepHasNothingToWaitFor()
    {
        // Arrange: steps are a sequence, not a graph.
        var path = Write();

        // Act.
        var result = await SetDependencies(path, "Build/Compile/step-0", ["Lint"]);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("run in order", result.Error);
    }

    [Fact]
    public async Task DisablingAStep_WritesEnabledFalseAndIsUndoable()
    {
        // Arrange.
        var path = Write();
        var before = Text(path);

        // Act.
        var result = await SetEnabled(path, "Build/Compile/step-0", enabled: false);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.True(Model(path).Steps.First().Execution.IsDisabled);

        // Act.
        await UndoAsync(result.Inverse!);

        // Assert.
        Assert.Equal(before, Text(path));
    }

    [Fact]
    public async Task EnablingSomething_RemovesTheKeyRatherThanWritingTrue()
    {
        // Arrange: the schema's default is enabled, so `enabled: true` says nothing the file did
        // not already say - and a pipeline collecting one per toggle is noisier for it.
        var path = Write();
        await SetEnabled(path, "Build/Compile/step-0", enabled: false);

        // Act.
        await SetEnabled(path, "Build/Compile/step-0", enabled: true);

        // Assert.
        Assert.DoesNotContain("enabled:", Text(path));
    }

    [Fact]
    public async Task AddingAStage_ProducesSomethingThatWouldActuallyRun()
    {
        // Arrange: a stage with no jobs is a schema error, so adding an empty one would hand the
        // user a pipeline that fails at queue time - the diagram "worked" and the build did not.
        var path = Write();

        // Act.
        var result = await Add(path, PipelineAddKind.Stage, name: "Ship");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var stage = Model(path).Stages.Single(candidate => candidate.Name == "Ship");
        Assert.NotEmpty(stage.Jobs);
        Assert.NotEmpty(stage.Jobs[0].Steps);
    }

    [Fact]
    public async Task AddingAStage_IsUndoneByRemovingIt()
    {
        // Arrange.
        var path = Write();
        var before = Text(path);

        // Act.
        var result = await Add(path, PipelineAddKind.Stage, name: "Ship");
        await UndoAsync(result.Inverse!);

        // Assert.
        Assert.Equal(before, Text(path));
    }

    [Fact]
    public async Task AddingAStageWithATakenName_PicksAFreeOne()
    {
        // Arrange: Azure matches dependsOn by name, so two stages sharing one makes the pipeline's
        // ordering ambiguous rather than merely untidy.
        var path = Write();

        // Act.
        await Add(path, PipelineAddKind.Stage, name: "Build");

        // Assert.
        Assert.Contains(Model(path).Stages, stage => stage.Name == "Build2");
    }

    [Fact]
    public async Task AddingAJob_JoinsTheStageItWasDroppedOn()
    {
        // Arrange & act.
        var path = Write();
        var result = await Add(path, PipelineAddKind.Job, parentId: "Test", name: "Smoke");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(Model(path).Stages.Single(stage => stage.Name == "Test").Jobs, job => job.Name == "Smoke");
    }

    [Fact]
    public async Task AddingADeploymentJob_WritesTheEnvironmentAndStrategyItNeeds()
    {
        // Arrange: a deployment without both does not run.
        var path = Write();

        // Act.
        var result = await Add(path, PipelineAddKind.DeploymentJob, parentId: "Test", name: "Release");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var job = Model(path).Jobs.Single(candidate => candidate.Name == "Release");
        Assert.True(job.IsDeployment);
        Assert.NotEmpty(job.Environment);
        Assert.Equal(PipelineStrategyKind.RunOnce, job.Strategy.Kind);
        Assert.NotEmpty(job.Steps);
    }

    [Fact]
    public async Task AddingAJobToAStep_IsRefused()
    {
        // Arrange: a job goes in a stage, and dropping is only as good as what it refuses.
        var path = Write();

        // Act.
        var result = await Add(path, PipelineAddKind.Job, parentId: "Build/Compile/step-0");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("goes in a stage", result.Error);
    }

    [Fact]
    public async Task AddingAStepToAStage_IsRefused()
    {
        // Arrange & act.
        var path = Write();
        var result = await Add(path, PipelineAddKind.Step, parentId: "Build");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("goes in a job", result.Error);
    }

    [Fact]
    public async Task AddingAStep_JoinsTheEndOfItsJob()
    {
        // Arrange & act.
        var path = Write();
        var result = await Add(path, PipelineAddKind.Step, parentId: "Build/Compile");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var steps = Model(path).Jobs.Single(job => job.Name == "Compile").Steps;
        Assert.Equal(3, steps.Count);
        Assert.Contains(PipelineBlocks.PlaceholderScript, steps[^1].Identifier);
    }

    [Fact]
    public async Task RemovingAStage_PutsBackExactlyWhatWasThere()
    {
        // Arrange: the undo of a remove is not an add. An add writes a fresh block and would
        // return something merely resembling what went - without its conditions or its comments.
        var path = Write("""
            stages:
              - stage: Build
                jobs:
                  - job: Compile
                    steps:
                      - script: x

              # This comment belongs to Ship, and an add would not bring it back.
              - stage: Ship
                dependsOn: Build
                condition: always()
                jobs:
                  - job: Push
                    steps:
                      - script: y
            """);
        var before = Text(path);

        // Act.
        var result = await Remove(path, "Ship");
        Assert.True(result.IsSuccess, result.Error);
        await UndoAsync(result.Inverse!);

        // Assert.
        Assert.Equal(before, Text(path));
    }

    [Fact]
    public async Task RemovingTheLastStage_IsRefused()
    {
        // Arrange & act.
        var path = Write("stages:\n  - stage: Only\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");
        var result = await Remove(path, "Only");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("at least one stage", result.Error);
    }

    [Fact]
    public async Task RemovingAJobsLastStep_IsRefused()
    {
        // Arrange: a job with no steps does not queue.
        var path = Write();

        // Act.
        var result = await Remove(path, "Build/Lint/step-0");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("at least one step", result.Error);
    }

    [Fact]
    public async Task RemovingAStagesLastJob_IsRefused()
    {
        // Arrange & act.
        var path = Write();
        var result = await Remove(path, "Test/Verify");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("at least one job", result.Error);
    }

    [Fact]
    public async Task RemovingAStep_LeavesTheRestOfTheJobAlone()
    {
        // Arrange & act.
        var path = Write();
        var result = await Remove(path, "Build/Compile/step-0");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var steps = Model(path).Jobs.Single(job => job.Name == "Compile").Steps;
        Assert.Single(steps);
        Assert.Equal("Pack", steps[0].Label);
    }

    [Fact]
    public async Task ReorderingSteps_ChangesTheOrderTheyRunIn()
    {
        // Arrange: steps are the one thing whose order is the whole of their meaning.
        var path = Write();

        // Act.
        var result = await MoveStep(path, "Build/Compile/step-1", 0);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            ["Pack", "Build"],
            Model(path).Jobs.Single(job => job.Name == "Compile").Steps.Select(step => step.Label));
    }

    [Fact]
    public async Task ReorderingSteps_IsUndoable()
    {
        // Arrange.
        var path = Write();
        var before = Text(path);

        // Act.
        var result = await MoveStep(path, "Build/Compile/step-1", 0);
        await UndoAsync(result.Inverse!);

        // Assert.
        Assert.Equal(before, Text(path));
    }

    [Fact]
    public async Task MovingAStepNowhere_DoesNothing()
    {
        // Arrange & act.
        var path = Write();
        var result = await MoveStep(path, "Build/Compile/step-0", 0);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Null(result.Inverse);
    }

    [Fact]
    public async Task AnEditToATemplateSourcedElement_IsRefusedNamingTheTemplate()
    {
        // Arrange: Requirement 5.4 - its text lives in another file, so the edit would land in
        // the wrong one. The message says where it can be made instead.
        Directory.CreateDirectory(IoPath.Combine(_workspace, "templates"));
        await File.WriteAllTextAsync(IoPath.Combine(_workspace, "templates", "jobs.yml"), "jobs:\n  - job: FromTemplate\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);
        var path = Write("stages:\n  - stage: Build\n    jobs:\n      - template: templates/jobs.yml\n");

        // Act.
        var result = await Rename(path, "Build/FromTemplate", "Renamed");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("templates/jobs.yml", result.Error);
    }

    [Fact]
    public async Task AnEditToAFileThatDoesNotParse_IsRefusedRatherThanMakingItWorse()
    {
        // Arrange.
        var path = Write("stages:\n  - stage: Build\n   jobs: [\n");

        // Act.
        var result = await Rename(path, "Build", "Anything");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("Line ", result.Error);
    }

    [Fact]
    public async Task EveryEdit_LeavesAPipelineThatStillParses()
    {
        // Arrange: the worst outcome available is an edit that produces a file this module can no
        // longer read, so every command is run in turn against one document.
        var path = Write();

        // Act.
        Assert.True((await Rename(path, "Build", "Build it")).IsSuccess);
        Assert.True((await SetEnabled(path, "Build/Lint", enabled: false)).IsSuccess);
        Assert.True((await Add(path, PipelineAddKind.Stage, name: "Ship")).IsSuccess);
        Assert.True((await Add(path, PipelineAddKind.Job, parentId: "Ship", name: "Push")).IsSuccess);
        Assert.True((await Add(path, PipelineAddKind.Step, parentId: "Ship/Push")).IsSuccess);
        Assert.True((await MoveStep(path, "Build/Compile/step-1", 0)).IsSuccess);
        Assert.True((await SetDependencies(path, "Ship", ["Test"])).IsSuccess);

        // Assert.
        var entry = _store.GetOrLoad(_workspace, path);
        Assert.True(entry.IsUsable, entry.Error);
        Assert.Equal(["Build", "Test", "Ship"], entry.Model.Stages.Select(stage => stage.Name));
        Assert.Equal(["Test"], entry.Model.Stages.Single(stage => stage.Name == "Ship").DependsOn);
    }
}
