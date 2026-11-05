using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// A renamed task keeps the position stored for it (databricks-disl-fbl finding B16, Requirement 6.6): the layout entry of
/// its old id moves to its new one in the same step, and one undo restores the body and the registration together.
/// </summary>
public sealed class DatabricksRenameKeepsPositionTests : IDisposable
{
    private const string Fixture = "job.yml";

    private readonly TestFolder _testFolder = TestFolder.Create("databricks-rename-position");
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly DatabricksContextActionProvider _actions;
    private readonly string _body;
    private readonly string _registration;

    public DatabricksRenameKeepsPositionTests()
    {
        _root = _testFolder.Path;
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        _actions = new DatabricksContextActionProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IDatabricksDocumentStore>());
        _body = IoPath.Combine(_root, Fixture);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", Fixture), _body);
        _registration = IoPath.Combine(_root, "job.adp");
        File.WriteAllText(_registration, $"databricks/job\r\nbody: {Fixture}\r\nlayout:\r\n  task:ingest: 10 20\r\n  task:quality_gate: 200 100\r\n");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task ARenamedTask_IsDrawnWhereItWasStored()
    {
        // Act.
        var renamed = await RenameAsync("quality_gate", "gate");

        // Assert.
        Assert.True(renamed.Completed, renamed.Error);
        await using var session = Open();
        var elements = await ElementsAsync(session);
        var gate = Assert.Single(elements, element => element.Id == "task:gate");
        Assert.Equal((200d, 100d), (gate.X, gate.Y));
        var layout = RegistrationLayout.Read(_registration);
        Assert.False(layout.ContainsKey("task:quality_gate"));
        Assert.Equal(new RegistrationPosition(10, 20), layout["task:ingest"]);
    }

    [Fact]
    public async Task OneUndo_RestoresTheBodyAndTheRegistration()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(_body, TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken);
        var renamed = await RenameAsync("quality_gate", "gate");
        Assert.True(renamed.Completed, renamed.Error);

        // Act.
        var undone = await _provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_root)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(body, await File.ReadAllBytesAsync(_body, TestContext.Current.CancellationToken));
        Assert.Equal(registration, await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARenameOfATaskWithNoStoredPosition_LeavesTheRegistrationAsItWas()
    {
        // Arrange.
        var registration = await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken);

        // Act.
        var renamed = await RenameAsync("publish", "release");

        // Assert.
        Assert.True(renamed.Completed, renamed.Error);
        Assert.Equal(registration, await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARename_ChangesOnlyTheEntryItCarries_AndKeepsAStaleOne()
    {
        // Arrange. Line feeds, the author's number form and an entry for a task the file does not have are the author's,
        // and stay: the registration writer takes out the renamed task's entry and prunes nothing else (Requirement 5.5).
        await File.WriteAllTextAsync(_registration, $"databricks/job\nbody: {Fixture}\nlayout:\n  task:ingest: 10.50 20\n  task:quality_gate: 200 100\n  task:retired: 5 5\n", TestContext.Current.CancellationToken);

        // Act.
        var renamed = await RenameAsync("quality_gate", "gate");

        // Assert.
        Assert.True(renamed.Completed, renamed.Error);
        Assert.Equal(
            $"databricks/job\nbody: {Fixture}\nlayout:\n  task:gate: 200 100\n  task:ingest: 10.50 20\n  task:retired: 5 5\n",
            await File.ReadAllTextAsync(_registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARenameUndoneAndRedone_GivesTheSameBytesEachTime()
    {
        // Arrange.
        var before = await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken);
        Assert.True((await RenameAsync("quality_gate", "gate")).Completed);
        var after = await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken);
        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_root));

        // Act.
        var undone = await history.UndoAsync(TestContext.Current.CancellationToken);
        var undoneBytes = await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken);
        var redone = await history.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.True(redone.IsSuccess, redone.Error);
        Assert.Equal(before, undoneBytes);
        Assert.Equal(after, await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken));
    }

    private ValueTask<ContextCommitResult> RenameAsync(string key, string newKey) =>
        _actions.CommitAsync(
            ContextTarget.ForElement(_body, $"task:{key}", ProjectRoot.From(_root), origin: ServiceCollectionAddDatabricksExtension.JobOrigin) with { RegistrationPath = _registration },
            DatabricksContextActionProvider.RenameTaskActionId,
            newKey,
            "",
            TestContext.Current.CancellationToken);

    private IDiagramSession Open()
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>().Single(candidate => candidate.Origin.Key == ServiceCollectionAddDatabricksExtension.JobOrigin.Key);
        return factory.Open(ShortGuid.NewShortGuid(), ProjectRoot.From(_root), _body, _registration);
    }

    private static async Task<List<DiagramElement>> ElementsAsync(IDiagramSession session) =>
        [.. (await session.BaselineAsync(TestContext.Current.CancellationToken)).OfType<DiagramAddDelta>().SelectMany(add => add.Elements)];
}
