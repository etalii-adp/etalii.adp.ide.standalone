using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// What a user is offered on a pipeline element.
/// </summary>
/// <remarks>
/// The rule running through all of these is that an action which would fail is not offered. The
/// commands already refuse an edit that would produce a pipeline Azure DevOps will not run; the
/// provider asks the same questions first, so the answer is a missing menu item rather than an
/// error message after the click.
/// </remarks>
public class PipelineContextActionProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-actions-" + Guid.NewGuid().ToString("N"));

    private readonly PipelineDocumentStore _store = new();
    private readonly PipelineViewState _views = new();
    private readonly HistoryStackStore _historyStacks;
    private readonly PipelineContextActionProvider _provider;

    public PipelineContextActionProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new PipelineTestDispatcher(_store));
        _provider = new PipelineContextActionProvider(_historyStacks, _store, _views);
    }

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
                  - script: dotnet pack
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

    /// <summary>
    /// Writes the pipeline afresh and makes the store forget whatever it had.
    /// </summary>
    /// <remarks>
    /// The forget is not optional. The store caches by path, so rewriting the file behind its back
    /// leaves it serving the model from before - and a test that discovered actions against a
    /// stale model and committed them against a fresh one would report a disagreement between the
    /// provider and the commands that neither of them actually has.
    /// </remarks>
    private string Write(string content = Pipeline)
    {
        var path = IoPath.Combine(_workspace, "azure-pipelines.yml");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, ShortGuid.NewShortGuid(), elementId);

    private async Task<IReadOnlyList<string>> ActionsOn(string path, string elementId)
    {
        var groups = await _provider.DiscoverAsync(Target(path, elementId), CancellationToken.None);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    private async Task<ContextCommitResult> CommitAsync(string path, string elementId, string actionId, string value = "") =>
        await _provider.CommitAsync(Target(path, elementId), actionId, value, "", CancellationToken.None);

    [Fact]
    public void ItAnswersForDiagramElements()
    {
        // Assert.
        Assert.Equal(ContextScope.DiagramElement, _provider.Scope);
    }

    [Fact]
    public async Task AStage_IsOfferedTheEditsThatApplyToIt()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "Build");

        // Assert.
        Assert.Contains(PipelineContextActionProvider.RenameActionId, actions);
        Assert.Contains(PipelineContextActionProvider.ToggleEnabledActionId, actions);
        Assert.Contains(PipelineContextActionProvider.AddJobActionId, actions);
        Assert.Contains(PipelineContextActionProvider.AddDeploymentJobActionId, actions);
        Assert.Contains(PipelineContextActionProvider.AddStageActionId, actions);
    }

    [Fact]
    public async Task AStage_IsNotOfferedAddStep()
    {
        // Arrange: a step goes in a job. Offering it here would put the refusal one click too late.
        var path = Write();

        // Act.
        var actions = await ActionsOn(path, "Build");

        // Assert.
        Assert.DoesNotContain(PipelineContextActionProvider.AddStepActionId, actions);
    }

    [Fact]
    public async Task AJob_IsOfferedAddStepAndNotAddJob()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "Build/Compile");

        // Assert.
        Assert.Contains(PipelineContextActionProvider.AddStepActionId, actions);
        Assert.DoesNotContain(PipelineContextActionProvider.AddJobActionId, actions);
    }

    [Fact]
    public async Task ActionsCarryTheirShortcutsAsData()
    {
        // Arrange: Requirement 9.7 - the client holds no key-to-action table, so the key travels
        // with the action or it does not exist.
        var path = Write();

        // Act.
        var groups = await _provider.DiscoverAsync(Target(path, "Build"), CancellationToken.None);
        var rename = groups.SelectMany(group => group.Actions)
            .Single(action => action.Id == PipelineContextActionProvider.RenameActionId);

        // Assert.
        Assert.Equal("F2", rename.Shortcut?.Key);
    }

    [Fact]
    public async Task TheOnlyStage_IsNotOfferedRemove()
    {
        // Arrange: a pipeline needs at least one stage.
        var path = Write("stages:\n  - stage: Only\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Act.
        var actions = await ActionsOn(path, "Only");

        // Assert.
        Assert.DoesNotContain(PipelineContextActionProvider.RemoveActionId, actions);
    }

    [Fact]
    public async Task OneOfSeveralStages_IsOfferedRemove()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "Test");

        // Assert.
        Assert.Contains(PipelineContextActionProvider.RemoveActionId, actions);
    }

    [Fact]
    public async Task AStagesOnlyJob_IsNotOfferedRemove()
    {
        // Arrange: a stage with no jobs does not queue.
        var path = Write();

        // Act.
        var actions = await ActionsOn(path, "Test/Verify");

        // Assert.
        Assert.DoesNotContain(PipelineContextActionProvider.RemoveActionId, actions);
    }

    [Fact]
    public async Task AJobsOnlyStep_IsNotOfferedRemove()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "Build/Lint/step-0");

        // Assert.
        Assert.DoesNotContain(PipelineContextActionProvider.RemoveActionId, actions);
    }

    [Fact]
    public async Task AFirstStep_IsNotOfferedMoveUp()
    {
        // Arrange: it has nowhere to go.
        var path = Write();

        // Act.
        var actions = await ActionsOn(path, "Build/Compile/step-0");

        // Assert.
        Assert.DoesNotContain(PipelineContextActionProvider.MoveStepUpActionId, actions);
        Assert.Contains(PipelineContextActionProvider.MoveStepDownActionId, actions);
    }

    [Fact]
    public async Task ALastStep_IsNotOfferedMoveDown()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "Build/Compile/step-1");

        // Assert.
        Assert.Contains(PipelineContextActionProvider.MoveStepUpActionId, actions);
        Assert.DoesNotContain(PipelineContextActionProvider.MoveStepDownActionId, actions);
    }

    [Fact]
    public async Task AStageWithNoDependsOn_IsNotOfferedToClearIt()
    {
        // Arrange: it is already on the default, so there is nothing to put back.
        var path = Write();

        // Act.
        var actions = await ActionsOn(path, "Build");

        // Assert.
        Assert.DoesNotContain(PipelineContextActionProvider.ClearDependenciesActionId, actions);
    }

    [Fact]
    public async Task AStageWithADependsOn_IsOfferedToClearIt()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "Test");

        // Assert.
        Assert.Contains(PipelineContextActionProvider.ClearDependenciesActionId, actions);
    }

    [Fact]
    public async Task AnElementFromATemplate_IsOfferedNothing()
    {
        // Arrange: Requirement 5.4 - its text is in another file, so every edit here would land
        // in the wrong one. Greying the actions out would still invite the click.
        Directory.CreateDirectory(IoPath.Combine(_workspace, "templates"));
        await File.WriteAllTextAsync(IoPath.Combine(_workspace, "templates", "jobs.yml"), "jobs:\n  - job: FromTemplate\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);
        var path = Write("stages:\n  - stage: Build\n    jobs:\n      - template: templates/jobs.yml\n");

        // Act.
        var actions = await ActionsOn(path, "Build/FromTemplate");

        // Assert.
        Assert.Empty(actions);
    }

    [Fact]
    public async Task AFileThatDoesNotParse_OffersNothing()
    {
        // Arrange: nothing may be edited in it, so nothing is offered on it.
        var path = Write("stages:\n  - stage: Build\n   jobs: [\n");

        // Act.
        var actions = await ActionsOn(path, "Build");

        // Assert.
        Assert.Empty(actions);
    }

    [Fact]
    public async Task AnElementThatIsGone_OffersNothing()
    {
        // Arrange & act.
        var path = Write();
        var actions = await ActionsOn(path, "NoSuchStage");

        // Assert.
        Assert.Empty(actions);
    }

    [Fact]
    public async Task ARename_AsksForTheNewNameStartingFromTheCurrentOne()
    {
        // Arrange: an edit starts from what is there rather than from an empty box.
        var path = Write("stages:\n  - stage: Build\n    displayName: Build it\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Act.
        var result = await _provider.ExecuteAsync(
            Target(path, "Build"),
            PipelineContextActionProvider.RenameActionId,
            CancellationToken.None);

        // Assert.
        var input = Assert.IsType<ContextExecutionRequiresInput>(result);
        Assert.Equal("Build it", input.Request.InitialValue);
    }

    [Fact]
    public async Task AnActionWithEverythingItNeeds_DoesNotPutADialogInTheWay()
    {
        // Arrange: disabling something asks nothing, so it should not stop to ask.
        var path = Write();

        // Act.
        var result = await _provider.ExecuteAsync(
            Target(path, "Build"),
            PipelineContextActionProvider.ToggleEnabledActionId,
            CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
    }

    [Fact]
    public async Task CommittingARename_WritesItThroughTheHistory()
    {
        // Arrange: Requirement 9.8 - one undo away, like every other edit in the IDE.
        var path = Write();

        // Act.
        var result = await CommitAsync(path, "Build", PipelineContextActionProvider.RenameActionId, "Build it");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Contains("displayName: Build it", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.True(_historyStacks.Get(_workspace).CanUndo);
    }

    [Fact]
    public async Task CommittingARename_IsUndoable()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await CommitAsync(path, "Build", PipelineContextActionProvider.RenameActionId, "Build it");

        // Act.
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TogglingEnabled_SwitchesTheElementOffAndBackOn()
    {
        // Arrange.
        var path = Write();

        // Act.
        await CommitAsync(path, "Build/Lint", PipelineContextActionProvider.ToggleEnabledActionId);

        // Assert.
        Assert.True(_store.GetOrLoad(_workspace, path).Model.Jobs.Single(job => job.Name == "Lint").Execution.IsDisabled);

        // Act: the action's label and effect both follow the current state, so committing again
        // turns it back on rather than off a second time.
        await CommitAsync(path, "Build/Lint", PipelineContextActionProvider.ToggleEnabledActionId);

        // Assert.
        Assert.False(_store.GetOrLoad(_workspace, path).Model.Jobs.Single(job => job.Name == "Lint").Execution.IsDisabled);
    }

    [Fact]
    public async Task TheToggleIsLabelledForWhatItWillDo()
    {
        // Arrange.
        var path = Write();

        // Act.
        var before = await LabelOf(path, "Build/Lint", PipelineContextActionProvider.ToggleEnabledActionId);
        await CommitAsync(path, "Build/Lint", PipelineContextActionProvider.ToggleEnabledActionId);
        var after = await LabelOf(path, "Build/Lint", PipelineContextActionProvider.ToggleEnabledActionId);

        // Assert.
        Assert.Equal("Disable", before);
        Assert.Equal("Enable", after);
    }

    [Fact]
    public async Task AddingAJobThroughTheMenu_UsesTheSameCommandTheToolboxWill()
    {
        // Arrange: Requirement 9.6 - one implementation behind all three triggers.
        var path = Write();

        // Act.
        var result = await CommitAsync(path, "Test", PipelineContextActionProvider.AddJobActionId);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(2, _store.GetOrLoad(_workspace, path).Model.Stages.Single(stage => stage.Name == "Test").Jobs.Count);
    }

    [Fact]
    public async Task MovingAStepDown_ReordersIt()
    {
        // Arrange & act.
        var path = Write();
        var result = await CommitAsync(path, "Build/Compile/step-0", PipelineContextActionProvider.MoveStepDownActionId);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(
            ["dotnet pack", "dotnet build"],
            _store.GetOrLoad(_workspace, path).Model.Jobs.Single(job => job.Name == "Compile").Steps.Select(step => step.Identifier));
    }

    [Fact]
    public async Task RemovingAStage_TakesItOut()
    {
        // Arrange & act.
        var path = Write();
        var result = await CommitAsync(path, "Test", PipelineContextActionProvider.RemoveActionId);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(["Build"], _store.GetOrLoad(_workspace, path).Model.Stages.Select(stage => stage.Name));
    }

    [Fact]
    public async Task ClearingDependencies_PutsTheStageBackOnTheDefault()
    {
        // Arrange & act.
        var path = Write();
        var result = await CommitAsync(path, "Test", PipelineContextActionProvider.ClearDependenciesActionId);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.False(_store.GetOrLoad(_workspace, path).Model.Stages.Single(stage => stage.Name == "Test").DependsOnDeclared);
    }

    [Fact]
    public async Task AnUnknownAction_IsRefusedRatherThanIgnored()
    {
        // Arrange & act.
        var path = Write();
        var result = await CommitAsync(path, "Build", "azure-pipeline.invent-a-stage");

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("does not apply", result.Error);
    }

    [Fact]
    public async Task EveryOfferedAction_ActuallyDoesSomething()
    {
        // Arrange: the promise this provider makes is that nothing offered will fail. Rather than
        // trusting the rules above one at a time, this walks every element of a pipeline, offers
        // what it offers, and puts each one through the path the service really takes - execute
        // first, and commit only where the execution asked for something.
        //
        // Taking a shortcut straight to commit is what this test used to do, and it reported a
        // failure that was its own: an action doing view-state work finishes in ExecuteAsync and
        // is never committed at all, so demanding a successful commit of it was asking the wrong
        // question of the right code.
        foreach (var elementId in AllElementIds())
        {
            foreach (var actionId in await ActionsOn(Write(), elementId))
            {
                // Arrange: a fresh document per action, so one edit does not invalidate the next.
                var path = Write();

                // Act.
                var executed = await _provider.ExecuteAsync(Target(path, elementId), actionId, CancellationToken.None);

                // Assert.
                Assert.False(
                    executed is ContextExecutionFailed,
                    $"{actionId} was offered on {elementId} but its execution failed.");

                if (executed is not ContextExecutionRequiresInput)
                {
                    // It did its work, so there is nothing left to commit.
                    continue;
                }

                // Act, continued: only an action that asked for a value goes on to commit one.
                var committed = await CommitAsync(path, elementId, actionId, "A new name");

                // Assert.
                Assert.True(committed.Completed, $"{actionId} was offered on {elementId} but failed: {committed.Error}");
            }
        }
    }

    [Fact]
    public async Task EveryActionThatWritesSomething_CommitsSuccessfully()
    {
        // Arrange: the half the test above no longer covers - every action that reaches a command
        // really does reach one, rather than falling through CommandFor and being refused.
        var writing = new[]
        {
            (PipelineContextActionProvider.ToggleEnabledActionId, "Build"),
            (PipelineContextActionProvider.ClearDependenciesActionId, "Test"),
            (PipelineContextActionProvider.RemoveActionId, "Test"),
            (PipelineContextActionProvider.AddStageActionId, "Build"),
            (PipelineContextActionProvider.AddJobActionId, "Build"),
            (PipelineContextActionProvider.AddDeploymentJobActionId, "Build"),
            (PipelineContextActionProvider.AddStepActionId, "Build/Compile"),
            (PipelineContextActionProvider.MoveStepDownActionId, "Build/Compile/step-0"),
            (PipelineContextActionProvider.RenameActionId, "Build"),
        };

        foreach (var (actionId, elementId) in writing)
        {
            // Act.
            var path = Write();
            var result = await CommitAsync(path, elementId, actionId, "A new name");

            // Assert.
            Assert.True(result.Completed, $"{actionId} on {elementId} failed: {result.Error}");
        }
    }

    /// <summary>Every element of the fixture pipeline, at all three levels.</summary>
    private IEnumerable<string> AllElementIds()
    {
        var model = _store.GetOrLoad(_workspace, Write()).Model;
        foreach (var stage in model.Stages)
        {
            yield return stage.Id;
            foreach (var job in stage.Jobs)
            {
                yield return job.Id;
                foreach (var step in job.Steps)
                {
                    yield return step.Id;
                }
            }
        }
    }

    private async Task<string> LabelOf(string path, string elementId, string actionId)
    {
        var groups = await _provider.DiscoverAsync(Target(path, elementId), CancellationToken.None);
        return groups.SelectMany(group => group.Actions).Single(action => action.Id == actionId).Label;
    }
}
