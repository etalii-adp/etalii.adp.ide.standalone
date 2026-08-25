using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The provider that offers undo and redo as project-scope context actions. Driven against a
/// real history from <see cref="TestHistory"/>, so what it reports is what a recorded command
/// actually leaves behind (diagram-undo-redo Requirement 5).
/// </summary>
public class HistoryContextActionProviderTests : IDisposable
{
    private readonly string _root;
    private readonly IHistoryStack _history;
    private readonly HistoryContextActionProvider _provider;

    public HistoryContextActionProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out var historyStacks);
        _provider = new HistoryContextActionProvider(historyStacks);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ContextTarget ProjectTarget() =>
        new(ContextScope.Project, _root, IsContainer: true, SourceId: default, RootPath: _root);

    private async Task<IReadOnlyList<ContextActionDefinition>> DiscoverAsync() =>
        (await _provider.DiscoverAsync(ProjectTarget(), TestContext.Current.CancellationToken)).SelectMany(g => g.Actions).ToList();

    /// <summary>Records one real, reversible change so the history has something to undo.</summary>
    private async Task RecordARenameAsync()
    {
        var path = IoPath.Combine(_root, "before.txt");
        File.WriteAllText(path, "content");
        var result = await _history.ExecuteAsync(new RenameEntryCommand(path, "after.txt"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public void Scope_IsProject()
    {
        Assert.Equal(ContextScope.Project, _provider.Scope);
    }

    [Fact]
    public async Task DiscoverAsync_OnAnEmptyHistory_OffersBothUnavailableWithTheirReasons()
    {
        var actions = await DiscoverAsync();

        var undo = actions.Single(a => a.Id == HistoryContextActionProvider.UndoActionId);
        var redo = actions.Single(a => a.Id == HistoryContextActionProvider.RedoActionId);
        Assert.False(undo.Available);
        Assert.False(redo.Available);
        Assert.Equal("There is nothing to undo.", undo.UnavailableReason);
        Assert.Equal("There is nothing to redo.", redo.UnavailableReason);
    }

    [Fact]
    public async Task DiscoverAsync_CarriesTheFreeplaneShortcuts()
    {
        var actions = await DiscoverAsync();

        var undo = actions.Single(a => a.Id == HistoryContextActionProvider.UndoActionId);
        var redo = actions.Single(a => a.Id == HistoryContextActionProvider.RedoActionId);
        Assert.Equal("Z", undo.Shortcut?.Key);
        Assert.True(undo.Shortcut?.Ctrl);
        Assert.Equal("Y", redo.Shortcut?.Key);
        Assert.True(redo.Shortcut?.Ctrl);
        Assert.Equal("mdi-undo", undo.Icon);
        Assert.Equal("mdi-redo", redo.Icon);
    }

    [Fact]
    public async Task DiscoverAsync_AfterARecordedCommand_ReportsUndoAvailableAndRedoNot()
    {
        await RecordARenameAsync();

        var actions = await DiscoverAsync();

        var undo = actions.Single(a => a.Id == HistoryContextActionProvider.UndoActionId);
        var redo = actions.Single(a => a.Id == HistoryContextActionProvider.RedoActionId);
        Assert.True(undo.Available);
        Assert.Equal("", undo.UnavailableReason);
        Assert.False(redo.Available);
    }

    [Fact]
    public async Task DiscoverAsync_AfterAnUndo_ReportsRedoAvailable()
    {
        await RecordARenameAsync();
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        var actions = await DiscoverAsync();

        Assert.True(actions.Single(a => a.Id == HistoryContextActionProvider.RedoActionId).Available);
    }

    [Fact]
    public async Task ExecuteAsync_Undo_PerformsTheUndo()
    {
        await RecordARenameAsync();

        var result = await _provider.ExecuteAsync(ProjectTarget(), HistoryContextActionProvider.UndoActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.True(File.Exists(IoPath.Combine(_root, "before.txt")));
        Assert.False(File.Exists(IoPath.Combine(_root, "after.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Redo_ReappliesTheUndoneCommand()
    {
        await RecordARenameAsync();
        await _provider.ExecuteAsync(ProjectTarget(), HistoryContextActionProvider.UndoActionId, TestContext.Current.CancellationToken);

        var result = await _provider.ExecuteAsync(ProjectTarget(), HistoryContextActionProvider.RedoActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.True(File.Exists(IoPath.Combine(_root, "after.txt")));
        Assert.False(File.Exists(IoPath.Combine(_root, "before.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_UndoOnAnEmptyHistory_Fails()
    {
        var result = await _provider.ExecuteAsync(ProjectTarget(), HistoryContextActionProvider.UndoActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionFailed>(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnUnknownActionId_Fails()
    {
        var result = await _provider.ExecuteAsync(ProjectTarget(), "history.something-else", TestContext.Current.CancellationToken);

        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Contains("Unknown action", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAndCommit_AreNotSupported()
    {
        // Neither action prompts, so reaching either is a programming error, not a user answer.
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await _provider.ValidateAsync(ProjectTarget(), HistoryContextActionProvider.UndoActionId, "", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await _provider.CommitAsync(ProjectTarget(), HistoryContextActionProvider.UndoActionId, "", "", TestContext.Current.CancellationToken));
    }
}
