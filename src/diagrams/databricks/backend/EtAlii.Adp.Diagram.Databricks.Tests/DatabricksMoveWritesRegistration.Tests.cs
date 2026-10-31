using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// A move writes the registration through the FBL library's registration writer (databricks-disl-fbl Requirement 5.5):
/// one entry spliced in or changed, every other byte of the registration left as it was, one step of the history, and
/// no stale entry pruned (ruled by the user in chat, 2026-10-11).
/// </summary>
public sealed class DatabricksMoveWritesRegistrationTests : IDisposable
{
    private const string Fixture = "job.yml";

    private readonly TestFolder _testFolder = TestFolder.Create("databricks-move-registration");
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly string _body;
    private readonly string _registration;

    public DatabricksMoveWritesRegistrationTests()
    {
        _root = _testFolder.Path;
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        _body = IoPath.Combine(_root, Fixture);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", Fixture), _body);
        _registration = IoPath.Combine(_root, "job.adp");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task AMove_KeepsALayoutEntryForAnIdTheFileDoesNotHave()
    {
        // Arrange. The job has no task "retired": its entry is stale, and FBL 8.5 would prune it were the ids known.
        await WriteRegistrationAsync($"databricks/job\nbody: {Fixture}\nlayout:\n  task:retired: 5 5\n");

        // Act.
        var refusal = await MoveAsync("task:ingest", 120, 240);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            $"databricks/job\nbody: {Fixture}\nlayout:\n  task:ingest: 120 240\n  task:retired: 5 5\n",
            await File.ReadAllTextAsync(_registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMove_ChangesOnlyTheEntryItWrites()
    {
        // Arrange. Line feeds, a number in the author's own form and an entry out of order are the author's, and stay.
        await WriteRegistrationAsync($"databricks/job\nbody: {Fixture}\nlayout:\n  task:publish: 10.50 20\n  task:aaa: 1 2\n");

        // Act.
        var refusal = await MoveAsync("task:ingest", 120.25, 80);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            $"databricks/job\nbody: {Fixture}\nlayout:\n  task:ingest: 120.25 80\n  task:publish: 10.50 20\n  task:aaa: 1 2\n",
            await File.ReadAllTextAsync(_registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMove_IsOneUndoAway_AndTheUndoGivesTheBytesBack()
    {
        // Arrange.
        await WriteRegistrationAsync($"databricks/job\nbody: {Fixture}\nlayout:\n  task:ingest: 10.50 20\n");
        var before = await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken);
        var body = await File.ReadAllBytesAsync(_body, TestContext.Current.CancellationToken);
        Assert.Equal("", await MoveAsync("task:ingest", 300, 40));

        // Act.
        var undone = await _provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_root)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(_registration, TestContext.Current.CancellationToken));
        Assert.Equal(body, await File.ReadAllBytesAsync(_body, TestContext.Current.CancellationToken));
    }

    private Task WriteRegistrationAsync(string text) =>
        File.WriteAllTextAsync(_registration, text, TestContext.Current.CancellationToken);

    private async Task<string> MoveAsync(string elementId, double x, double y)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>().Single(candidate => candidate.Origin.Key == ServiceCollectionAddDatabricksExtension.JobOrigin.Key);
        await using var session = factory.Open(ShortGuid.NewShortGuid(), ProjectRoot.From(_root), _body, _registration);
        return await session.MoveElementToAsync(elementId, x, y, TestContext.Current.CancellationToken);
    }
}
