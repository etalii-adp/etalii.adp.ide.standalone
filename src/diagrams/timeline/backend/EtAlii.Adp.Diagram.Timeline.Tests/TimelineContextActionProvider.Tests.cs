using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The actions, walked along the real discover-then-execute-then-commit path rather than
/// asserted against a cached model.
/// </summary>
public class TimelineContextActionProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-actions-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineContextActionProvider _actions;
    private readonly HistoryStackStore _historyStacks;

    public TimelineContextActionProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new TimelineTestDispatcher(_store));
        _actions = new TimelineContextActionProvider(_historyStacks, _store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private const string Timeline = """
        timeline: 1
        elements:
          - id: aaa
            label: Period
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: bbb
            label: Moment
            begin: 2026-02-16
            row: 2
        connections:
          - id: ccc
            from: aaa
            to: bbb
            label: gates
        """;

    private string Write()
    {
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, Timeline);
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, ShortGuid.NewShortGuid(), elementId);

    private async Task<IReadOnlyList<string>> ActionIdsFor(string path, string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(path, elementId), CancellationToken.None);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    [Fact]
    public async Task APeriod_OffersRenameConnectRemoveEndAndRemove()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, "aaa");

        // Assert.
        Assert.Equal(
            [
                TimelineContextActionProvider.RenameActionId,
                TimelineContextActionProvider.ConnectActionId,
                TimelineContextActionProvider.RemoveEndActionId,
                TimelineContextActionProvider.RemoveActionId,
            ],
            ids);
    }

    [Fact]
    public async Task AMoment_OffersGiveEndInsteadOfRemoveEnd()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, "bbb");

        // Assert.
        Assert.Contains(TimelineContextActionProvider.GiveEndActionId, ids);
        Assert.DoesNotContain(TimelineContextActionProvider.RemoveEndActionId, ids);
    }

    [Fact]
    public async Task AConnection_OffersRelabelAndDisconnect()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, "ccc");

        // Assert.
        Assert.Equal(
            [TimelineContextActionProvider.RelabelActionId, TimelineContextActionProvider.DisconnectActionId],
            ids);
    }

    [Fact]
    public async Task AnotherTypesFile_GetsNothing()
    {
        // Arrange.
        // A provider is consulted for every element in its scope, including other types'. One
        // that parses a foreign file to answer has already gone wrong.
        var foreign = IoPath.Combine(_workspace, "map.owm");
        File.WriteAllText(foreign, "title something\n");

        // Act.
        var groups = await _actions.DiscoverAsync(Target(foreign, "x"), CancellationToken.None);

        // Assert.
        Assert.Empty(groups);
    }

    [Fact]
    public async Task RemovingAConnectedElement_AsksFirst_AndNamesTheCount()
    {
        // Arrange & act.
        // Requirement 2.5: the action says how many connections go with it, before it runs.
        var path = Write();
        var result = await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.RemoveActionId, CancellationToken.None);

        // Assert.
        var confirmation = Assert.IsType<ContextExecutionRequiresConfirmation>(result);
        Assert.Contains("1 connection", confirmation.Request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveCommitted_TakesTheElementAndItsConnections_OneUndoAway()
    {
        // Arrange.
        var path = Write();
        var before = File.ReadAllText(path);

        // Act.
        var commit = await _actions.CommitAsync(Target(path, "aaa"), TimelineContextActionProvider.RemoveActionId, "", "", CancellationToken.None);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.True(commit.Completed, commit.Error);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task Rename_AsksForTheLabel_ThenCommitsIt()
    {
        // Arrange.
        var path = Write();

        // Act.
        var ask = await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.RenameActionId, CancellationToken.None);
        var commit = await _actions.CommitAsync(Target(path, "aaa"), TimelineContextActionProvider.RenameActionId, "Renamed", "", CancellationToken.None);

        // Assert.
        var input = Assert.IsType<ContextExecutionRequiresInput>(ask);
        Assert.Equal("Period", input.Request.InitialValue);
        Assert.True(commit.Completed, commit.Error);
        Assert.Equal("Renamed", _store.GetOrLoad(path).Model.Elements[0].Label);
    }

    [Fact]
    public async Task GiveEnd_ValidatesAsTyped_OnTheHandlersOwnTerms()
    {
        // Arrange.
        var path = Write();
        var target = Target(path, "bbb");

        // Act.
        var inverted = await _actions.ValidateAsync(target, TimelineContextActionProvider.GiveEndActionId, "2026-01-01", CancellationToken.None);
        var unreadable = await _actions.ValidateAsync(target, TimelineContextActionProvider.GiveEndActionId, "sideways", CancellationToken.None);
        var good = await _actions.ValidateAsync(target, TimelineContextActionProvider.GiveEndActionId, "2026-03-01", CancellationToken.None);

        // Assert.
        Assert.False(inverted.Valid);
        Assert.False(unreadable.Valid);
        Assert.True(good.Valid);
    }

    [Fact]
    public async Task GiveEndCommitted_MakesTheMomentAPeriod_AndUndoMakesItAMomentAgain()
    {
        // Arrange.
        var path = Write();

        // Act.
        var commit = await _actions.CommitAsync(Target(path, "bbb"), TimelineContextActionProvider.GiveEndActionId, "2026-03-01", "", CancellationToken.None);
        var after = _store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "bbb").IsPeriod;
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.True(commit.Completed, commit.Error);
        Assert.True(after);
        Assert.False(_store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "bbb").IsPeriod);
    }

    [Fact]
    public async Task Connect_CompletesWithNothingDispatched()
    {
        // Arrange.
        // Connecting is a canvas gesture; the menu action only puts the canvas into its connect
        // state, and the backend cannot click the second element for you.
        var path = Write();
        var before = File.ReadAllText(path);

        // Act.
        var result = await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task AnAddCommit_LandsAtTheCarriedPosition()
    {
        // Arrange.
        // The drop's position travels in the commit value as "seconds,row" - the placement
        // Requirement 9.3 refuses to discard in favour of a computed one.
        var path = Write();
        var seconds = TimelineScale.ToSeconds(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        // Act.
        var commit = await _actions.CommitAsync(
            Target(path, ""),
            TimelineContextActionProvider.AddMomentActionId,
            $"{seconds},5",
            "",
            CancellationToken.None);

        // Assert.
        Assert.True(commit.Completed, commit.Error);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Label == "New moment");
        Assert.Equal("2026-07-01", added.Begin.Text);
        Assert.Equal(5, added.Row);
        Assert.False(added.IsPeriod);
    }

    [Fact]
    public async Task AnAddWithNoPosition_IsRefusedRatherThanLandingSomewhereInvented()
    {
        // Arrange & act.
        var path = Write();
        var commit = await _actions.CommitAsync(
            Target(path, ""), TimelineContextActionProvider.AddPeriodActionId, "", "", CancellationToken.None);

        // Assert.
        Assert.False(commit.Completed);
    }

    [Fact]
    public async Task DisconnectCommitted_IsOneUndoAway()
    {
        // Arrange.
        var path = Write();

        // Act.
        var commit = await _actions.CommitAsync(Target(path, "ccc"), TimelineContextActionProvider.DisconnectActionId, "", "", CancellationToken.None);
        var afterDisconnect = _store.GetOrLoad(path).Model.Connections.Count;
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.True(commit.Completed, commit.Error);
        Assert.Equal(0, afterDisconnect);
        Assert.Single(_store.GetOrLoad(path).Model.Connections);
    }

    [Fact]
    public async Task AnActionOnTheWrongSelection_ReportsItDoesNotApply()
    {
        // Arrange & act.
        // Requirement 11.8: unavailable with a reason, through the mechanism that already exists.
        var path = Write();
        var commit = await _actions.CommitAsync(Target(path, "ccc"), TimelineContextActionProvider.GiveEndActionId, "2026-03-01", "", CancellationToken.None);

        // Assert.
        Assert.False(commit.Completed);
        Assert.Contains("does not apply", commit.Error, StringComparison.Ordinal);
    }
}
