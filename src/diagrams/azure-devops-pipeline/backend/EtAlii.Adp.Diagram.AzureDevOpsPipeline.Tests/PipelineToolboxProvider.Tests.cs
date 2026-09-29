using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The toolbox, whose whole job is to carry data rather than behaviour: each entry names a context
/// action, and dropping it runs that action. The tests that matter here are therefore about the
/// join - that every entry names an action that exists, and that a drop really does end at the
/// same command the menu does (Requirement 9.6).
/// </summary>
public class PipelineToolboxProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-toolbox-" + Guid.NewGuid().ToString("N"));

    private readonly PipelineDocumentStore _store = new();
    private readonly PipelineViewState _views = new();
    private readonly PipelineToolboxProvider _toolbox = new();
    private readonly PipelineContextActionProvider _actions;

    public PipelineToolboxProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _actions = new PipelineContextActionProvider(new HistoryStackStore(new PipelineTestDispatcher(_store)), _store, _views);
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
        """;

    private string Write()
    {
        var path = IoPath.Combine(_workspace, "azure-pipelines.yml");
        File.WriteAllText(path, Pipeline);
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, ShortGuid.NewShortGuid(), elementId);

    /// <summary>Dropping an entry on an element is committing the action the entry names.</summary>
    private async Task<ContextCommitResult> DropAsync(string entryId, string path, string ontoElementId)
    {
        var item = _toolbox.Items.Single(candidate => candidate.Id == entryId);
        return await _actions.CommitAsync(Target(path, ontoElementId), item.DropActionId, "", "", CancellationToken.None);
    }

    [Fact]
    public void ItDescribesTheFourThingsThatCanBeAdded()
    {
        // Assert.
        Assert.Equal(
            ["Stage", "Job", "Deployment job", "Script step"],
            _toolbox.Items.Select(item => item.Label));
    }

    [Fact]
    public void ItAnswersForThisDiagramType()
    {
        // Assert.
        // The toolbox is resolved by origin, so a mismatch here is an empty palette rather than
        // an error anybody would see.
        Assert.Equal(Diagram.Pipeline.Origin, _toolbox.Origin);
    }

    [Fact]
    public void EveryEntryCarriesOnlyData()
    {
        // Assert.
        // The client renders a palette it does not understand, so an entry with no label, icon or
        // description is one the user cannot tell apart from the others.
        Assert.All(_toolbox.Items, item =>
        {
            ArgumentNullException.ThrowIfNull(item);

            Assert.NotEmpty(item.Id);
            Assert.NotEmpty(item.Label);
            Assert.NotEmpty(item.Icon);
            Assert.NotEmpty(item.Description);
            Assert.NotEmpty(item.DropActionId);
        });
    }

    [Fact]
    public void EveryEntryNamesAnActionThatExists()
    {
        // Arrange: the join between the two providers is a string, and a typo in it would be a
        // toolbox entry that silently does nothing when dropped.
        var known = typeof(PipelineContextActionProvider)
            .GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.All(_toolbox.Items, item => Assert.Contains(item.DropActionId, known));
    }

    [Fact]
    public void EveryEntrySaysWhereItGoes()
    {
        // Assert.
        // A drop that lands nowhere is a poor way to learn the rule, so the description states it.
        Assert.All(_toolbox.Items, item => Assert.Contains("Drop on a", item.Description, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryEntryHasItsOwnId()
    {
        // Assert.
        Assert.Equal(_toolbox.Items.Count, _toolbox.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DroppingAJobOnAStage_AddsOne()
    {
        // Arrange & act.
        var path = Write();
        var result = await DropAsync("azure-pipeline.toolbox.job", path, "Build");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(2, _store.GetOrLoad(_workspace, path).Model.Stages.Single().Jobs.Count);
    }

    [Fact]
    public async Task DroppingAStepOnAJob_AddsOne()
    {
        // Arrange & act.
        var path = Write();
        var result = await DropAsync("azure-pipeline.toolbox.step", path, "Build/Compile");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(2, _store.GetOrLoad(_workspace, path).Model.Jobs.Single().Steps.Count);
    }

    [Fact]
    public async Task DroppingAStageOnAStage_AddsAnotherAfterIt()
    {
        // Arrange & act.
        var path = Write();
        var result = await DropAsync("azure-pipeline.toolbox.stage", path, "Build");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(2, _store.GetOrLoad(_workspace, path).Model.Stages.Count);
    }

    [Fact]
    public async Task DroppingADeploymentJobOnAStage_AddsOneThatWouldRun()
    {
        // Arrange & act.
        var path = Write();
        var result = await DropAsync("azure-pipeline.toolbox.deployment-job", path, "Build");

        // Assert.
        Assert.True(result.Completed, result.Error);
        var job = _store.GetOrLoad(_workspace, path).Model.Jobs.Single(candidate => candidate.IsDeployment);
        Assert.NotEmpty(job.Environment);
        Assert.NotEmpty(job.Steps);
    }

    [Fact]
    public async Task DroppingAStepOnAStage_IsRefusedByTheSameRuleThatKeepsItOutOfTheMenu()
    {
        // Arrange: a step goes in a job. There is no second implementation here to disagree with
        // the menu, which is the whole reason the entry names an action rather than a command.
        var path = Write();

        // Act.
        var result = await DropAsync("azure-pipeline.toolbox.step", path, "Build");

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("goes in a job", result.Error);
    }

    [Fact]
    public async Task DroppingOnATemplateSourcedElement_IsRefused()
    {
        // Arrange: nothing here may be edited, and the drop goes through the same provider that
        // offers nothing on it.
        Directory.CreateDirectory(IoPath.Combine(_workspace, "templates"));
        await File.WriteAllTextAsync(IoPath.Combine(_workspace, "templates", "jobs.yml"), "jobs:\n  - job: FromTemplate\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);
        var path = IoPath.Combine(_workspace, "azure-pipelines.yml");
        await File.WriteAllTextAsync(path, "stages:\n  - stage: Build\n    jobs:\n      - template: templates/jobs.yml\n", TestContext.Current.CancellationToken);
        _store.Forget(path);

        // Act.
        var result = await DropAsync("azure-pipeline.toolbox.step", path, "Build/FromTemplate");

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("templates/jobs.yml", result.Error);
    }

    [Fact]
    public async Task ADropIsUndoneLikeAnyOtherEdit()
    {
        // Arrange: the drop inherits the action's command, so it inherits its undo too - there is
        // no separate drop path that could have been forgotten.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var historyStacks = new HistoryStackStore(new PipelineTestDispatcher(_store));
        var actions = new PipelineContextActionProvider(historyStacks, _store, _views);
        var item = _toolbox.Items.Single(candidate => candidate.Id == "azure-pipeline.toolbox.job");

        // Act.
        await actions.CommitAsync(Target(path, "Build"), item.DropActionId, "", "", CancellationToken.None);
        await historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
}
