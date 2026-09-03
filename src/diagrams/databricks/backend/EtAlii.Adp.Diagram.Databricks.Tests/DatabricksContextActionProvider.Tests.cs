using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Diagram;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The action surface: discovery per selection kind, every real edit one undo away through the
/// real command pipeline, and the simulated entries discovering with their marker while
/// executing without touching the history (databricks-diagrams Requirements 8 and 11).
/// </summary>
public class DatabricksContextActionProviderTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly DatabricksContextActionProvider _actions;
    private readonly IHistoryStack _history;

    public DatabricksContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddDatabricks()
            .BuildServiceProvider();
        _actions = new DatabricksContextActionProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IDatabricksDocumentStore>());
        _history = _provider.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    private ContextTarget Target(string bodyPath, string elementId) => new(
        ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, _root, default, elementId);

    private async Task<IReadOnlyList<string>> ActionIds(string bodyPath, string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(bodyPath, elementId), TestContext.Current.CancellationToken);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    [Fact]
    public async Task ATask_DiscoversItsMenu_IncludingPerEdgeDisconnectsAndTheSimulatedRun()
    {
        // Arrange.
        var body = CopyFixture("job.yml");

        // Act.
        var ids = await ActionIds(body, "task:publish");

        // Assert.
        Assert.Contains(DatabricksContextActionProvider.RenameTaskActionId, ids);
        Assert.Contains(DatabricksContextActionProvider.SetRunIfActionId, ids);
        Assert.Contains(DatabricksContextActionProvider.AssignClusterActionId, ids);
        Assert.Contains($"{DatabricksContextActionProvider.DisconnectActionPrefix}quality_gate", ids);
        Assert.Contains(DatabricksContextActionProvider.RemoveTaskActionId, ids);
        // The simulated entry carries its marker - the client's interception seam (Requirement 8.6).
        var simulated = Assert.Single(ids, id => id.Contains(DatabricksContextActionProvider.SimulatedMarker, StringComparison.Ordinal));
        Assert.Equal(DatabricksContextActionProvider.SimulatedRunActionId, simulated);
    }

    [Fact]
    public async Task DiscoveryPerKind_MatchesTheSelection()
    {
        // Arrange.
        var bundle = CopyFixture("bundle.yml");
        var pipeline = CopyFixture("pipeline.json");

        // Act & assert.
        Assert.Contains(DatabricksContextActionProvider.RenameBundleActionId, await ActionIds(bundle, "bundle"));
        Assert.Contains(DatabricksContextActionProvider.SimulatedDeployActionId, await ActionIds(bundle, "target:prod"));
        Assert.Contains(DatabricksContextActionProvider.SimulatedUpdateActionId, await ActionIds(pipeline, "pipeline"));
        Assert.Contains(DatabricksContextActionProvider.RemoveLibraryActionId, await ActionIds(pipeline, "library:transformations/silver.py"));
        Assert.Contains(DatabricksContextActionProvider.ConnectActionId, await ActionIds(CopyFixture("job.yml"), "rel:task:ingest->task:publish"));
        Assert.Empty(await ActionIds(bundle, "task:not-there"));
    }

    [Fact]
    public async Task RemoveTask_TakesItsEdges_AsOneUndo()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var before = File.ReadAllBytes(body);
        var target = Target(body, "task:quality_gate");

        // Act.
        // quality_gate touches three edges, so execution asks first; confirming commits.
        var execution = await _actions.ExecuteAsync(target, DatabricksContextActionProvider.RemoveTaskActionId, TestContext.Current.CancellationToken);
        Assert.IsType<ContextExecutionRequiresConfirmation>(execution);
        var commit = await _actions.CommitAsync(target, DatabricksContextActionProvider.RemoveTaskActionId, "", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        Assert.DoesNotContain("quality_gate", File.ReadAllText(body), StringComparison.Ordinal);
        // One undo brings back the task AND its edges, byte for byte (Requirement 11.5).
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, File.ReadAllBytes(body));
    }

    [Fact]
    public async Task Rename_RewritesEveryReference_AndRedoRedoes()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var before = File.ReadAllText(body);
        var target = Target(body, "task:quality_gate");

        // Act.
        var commit = await _actions.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, "gate", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(commit.Completed);
        Assert.DoesNotContain("quality_gate", File.ReadAllText(body), StringComparison.Ordinal);
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, File.ReadAllText(body));
        await _history.RedoAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("quality_gate", File.ReadAllText(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRelationGesture_ConnectsInOneCall_AndUndoReturnsTheBytes()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var before = File.ReadAllBytes(body);
        var gesture = Target(body, "rel:task:ingest->task:refresh_dashboard");

        // Act.
        var execution = await _actions.ExecuteAsync(gesture, DatabricksContextActionProvider.ConnectActionId, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(execution);
        Assert.Contains("- task_key: ingest", File.ReadAllText(body).Split("refresh_dashboard")[^1], StringComparison.Ordinal);
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, File.ReadAllBytes(body));
    }

    [Fact]
    public async Task ADropOnAPlacement_AddsTheTask_WithoutAsking()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var placement = Target(body, DatabricksNewPlacement.IdFor(400, 200));

        // Act.
        var execution = await _actions.ExecuteAsync(
            placement, $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(execution);
        Assert.Contains("task_key: notebook_1", File.ReadAllText(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASimulatedAction_CompletesAsANoOp_OffTheHistory()
    {
        // Arrange.
        var body = CopyFixture("job.yml");
        var bytes = File.ReadAllBytes(body);

        // Act.
        var execution = await _actions.ExecuteAsync(
            Target(body, "task:ingest"), DatabricksContextActionProvider.SimulatedRunActionId, TestContext.Current.CancellationToken);

        // Assert.
        // Requirement 11.6: nothing lands on the history, nothing touches the file.
        Assert.IsType<ContextExecutionCompleted>(execution);
        Assert.False(_history.CanUndo);
        Assert.Equal(bytes, File.ReadAllBytes(body));
    }

    [Fact]
    public async Task ADuplicateKey_IsRefusedInTheDialog_BeforeAnyCommit()
    {
        // Arrange.
        var body = CopyFixture("job.yml");

        // Act.
        var verdict = await _actions.ValidateAsync(
            Target(body, "task:ingest"), DatabricksContextActionProvider.RenameTaskActionId, "publish", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(verdict.Valid);
        Assert.Contains("already there", verdict.Reason, StringComparison.Ordinal);
    }
}
