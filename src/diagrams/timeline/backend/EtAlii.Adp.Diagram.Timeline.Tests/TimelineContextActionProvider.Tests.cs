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
        _actions = new TimelineContextActionProvider(_historyStacks, _store, new TimelineConnectState());
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

    // One watch id for the whole class: the relation gesture's two calls pair by watch, so a
    // helper that minted a fresh id per call would arm on one connection and complete on another.
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, _watchId, elementId);

    private async Task<IReadOnlyList<string>> ActionIdsFor(string path, string elementId)
    {
        var groups = await _actions.DiscoverAsync(Target(path, elementId), CancellationToken.None);
        return groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();
    }

    [Fact]
    public async Task AnElementWithAnEnd_OffersItsEditsAndItsAdditions()
    {
        // Arrange & act.
        var path = Write();
        var ids = await ActionIdsFor(path, "aaa");

        // Assert.
        // Two groups: what changes this element, then what adds the next one - the mindmap's
        // Insert/Enter pattern on this type's two axes.
        Assert.Equal(
            [
                TimelineContextActionProvider.RenameActionId,
                TimelineContextActionProvider.ConnectActionId,
                TimelineContextActionProvider.RemoveEndActionId,
                TimelineContextActionProvider.RemoveActionId,
                TimelineContextActionProvider.AddAfterActionId,
                TimelineContextActionProvider.AddBelowActionId,
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
        Assert.Contains("1 relation", confirmation.Request.Message, StringComparison.Ordinal);
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
    public async Task ADropOnAPlacement_LandsThereAtOnce()
    {
        // Arrange.
        // The drop names a placement - the synthetic id carrying where it landed - so nothing is
        // asked and the element appears at the dropped time and row (Requirement 9.3).
        var path = Write();
        var seconds = TimelineScale.ToSeconds(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, TimelineNewPlacement.IdFor(seconds, 5)),
            TimelineContextActionProvider.AddMomentActionId,
            CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Label == "New moment");
        Assert.Equal("2026-07-01", added.Begin.Text);
        Assert.Equal(5, added.Row);
        Assert.False(added.IsPeriod);
    }

    [Fact]
    public async Task AnAddFromAMenu_AsksForTheBegin_AndAGarbageValueIsRefused()
    {
        // Arrange & act.
        // No placement to land on: the begin is the one fact the add cannot guess.
        var path = Write();
        var ask = await _actions.ExecuteAsync(
            Target(path, "aaa"), TimelineContextActionProvider.AddElementActionId, CancellationToken.None);
        var commit = await _actions.CommitAsync(
            Target(path, "aaa"), TimelineContextActionProvider.AddElementActionId, "sideways", "", CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionRequiresInput>(ask);
        Assert.False(commit.Completed);
    }

    [Fact]
    public async Task Tab_AddsAnElementAfterTheSelectedOne()
    {
        // Arrange.
        // "After": a little later on the same row, a week long, and no dialog - everything is
        // derived from the selected element.
        var path = Write();

        // Act.
        var result = await _actions.ExecuteAsync(
            Target(path, "aaa"), TimelineContextActionProvider.AddAfterActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Label == "New element");
        Assert.Equal("2026-02-15", added.Begin.Text); // aaa ends 2026-02-13, plus the two-day gap
        Assert.Equal("2026-02-22", added.End!.Text);
        Assert.Equal(0, added.Row);
    }

    [Fact]
    public async Task Enter_AddsAnElementBelowTheSelectedOne()
    {
        // Arrange & act.
        // "Below": the same stretch of time, one row down - a parallel track.
        var path = Write();
        var result = await _actions.ExecuteAsync(
            Target(path, "aaa"), TimelineContextActionProvider.AddBelowActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Label == "New element");
        Assert.Equal("2026-01-05", added.Begin.Text);
        Assert.Equal("2026-02-13", added.End!.Text);
        Assert.Equal(1, added.Row);
    }

    [Fact]
    public async Task ARelationDraggedOntoEmptySpace_CreatesAndRelates_AsOneUndo()
    {
        // Arrange.
        // The gesture's second call names a placement rather than an element: what the relation
        // reaches does not exist yet, so it is created there and related in one command.
        var path = Write();
        var before = File.ReadAllText(path);
        var seconds = TimelineScale.ToSeconds(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        // Act.
        await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);
        var second = await _actions.ExecuteAsync(
            Target(path, TimelineNewPlacement.IdFor(seconds, 3)),
            TimelineContextActionProvider.ConnectActionId,
            CancellationToken.None);
        var model = _store.GetOrLoad(path).Model;
        var added = model.Elements.Single(element => element.Label == "New element");
        var relation = model.Connections.Single(candidate => candidate.To == added.Id);
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(second);
        Assert.Equal("2026-08-01", added.Begin.Text);
        Assert.Equal(3, added.Row);
        Assert.Equal("aaa", relation.From);
        // One undo takes the new element and its relation together, byte for byte.
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task ARelationOntoEmptySpaceWithNothingArmed_IsRefused()
    {
        // Arrange & act.
        var path = Write();
        var result = await _actions.ExecuteAsync(
            Target(path, TimelineNewPlacement.IdFor(0, 0)),
            TimelineContextActionProvider.ConnectActionId,
            CancellationToken.None);

        // Assert.
        var failed = Assert.IsType<ContextExecutionFailed>(result);
        Assert.Contains("starts from an element", failed.Message, StringComparison.Ordinal);
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

/// <summary>
/// The connect gesture as two calls through the one-element-per-call channel, and the add
/// dialog's value form.
/// </summary>
public class TimelineConnectGestureTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-connect-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineConnectState _connects = new();
    private readonly TimelineContextActionProvider _actions;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public TimelineConnectGestureTests()
    {
        Directory.CreateDirectory(_workspace);
        _actions = new TimelineContextActionProvider(
            new HistoryStackStore(new TimelineTestDispatcher(_store)), _store, _connects);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Write()
    {
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, "timeline: 1\r\nelements:\r\n  - id: aaa\r\n    begin: 2026-01-01\r\n    row: 0\r\n  - id: bbb\r\n    begin: 2026-02-01\r\n    row: 1\r\n");
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, _watchId, elementId);

    [Fact]
    public async Task ConnectOnTwoElements_MakesOneConnection()
    {
        // Arrange.
        var path = Write();

        // Act: the first call arms, the second completes.
        await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);
        var second = await _actions.ExecuteAsync(Target(path, "bbb"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(second);
        var connection = Assert.Single(_store.GetOrLoad(path).Model.Connections);
        Assert.Equal("aaa", connection.From);
        Assert.Equal("bbb", connection.To);
    }

    [Fact]
    public async Task ConnectTwiceOnTheSameElement_Cancels()
    {
        // Arrange.
        // A second tap on the same element reads as "never mind" - and a self-connection is
        // refused anyway (Requirement 8.7), so nothing is lost by cancelling instead.
        var path = Write();

        // Act.
        await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);
        await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        Assert.Empty(_store.GetOrLoad(path).Model.Connections);
        Assert.Null(_connects.PendingFor(_watchId, path));
    }

    [Fact]
    public async Task TwoConnectionsArmIndependently()
    {
        // Arrange.
        // The state is per connection: one viewer's half-drawn gesture is invisible to another.
        var path = Write();
        var otherWatch = ShortGuid.NewShortGuid();

        // Act.
        await _actions.ExecuteAsync(Target(path, "aaa"), TimelineContextActionProvider.ConnectActionId, CancellationToken.None);

        // Assert.
        Assert.Equal("aaa", _connects.PendingFor(_watchId, path));
        Assert.Null(_connects.PendingFor(otherWatch, path));
    }

    [Fact]
    public async Task AnAddCommittedWithADialogValue_LandsOnTheAnchorsRow()
    {
        // Arrange.
        // The dialog form: the value is the begin the user typed, and the row comes from the
        // element the gesture anchored on - the nearest the placement can travel while the
        // channel has no slot for a full one.
        var path = Write();

        // Act.
        var commit = await _actions.CommitAsync(
            Target(path, "bbb"), TimelineContextActionProvider.AddMomentActionId, "2026-06-01", "", CancellationToken.None);

        // Assert.
        Assert.True(commit.Completed, commit.Error);
        var added = _store.GetOrLoad(path).Model.Elements.Single(element => element.Label == "New moment");
        Assert.Equal("2026-06-01", added.Begin.Text);
        Assert.Equal(1, added.Row);
    }
}

/// <summary>
/// The guards for the two ways an action can exist and still do nothing: a target that
/// discovers no actions cannot have one executed by id, and a Completed execution that never
/// dispatched has not happened. Both were found in the running app, not by the unit tests,
/// because the unit tests called the provider directly and bypassed the by-id resolution.
/// </summary>
public class TimelineActionRealityTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-reality-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly TimelineContextActionProvider _actions;
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();

    public TimelineActionRealityTests()
    {
        Directory.CreateDirectory(_workspace);
        _actions = new TimelineContextActionProvider(
            new HistoryStackStore(new TimelineTestDispatcher(_store)), _store, new TimelineConnectState());
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Write()
    {
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, "timeline: 1\r\nelements:\r\n  - id: lone\r\n    label: Lone\r\n    begin: 2026-01-05\r\n    end: 2026-02-13\r\n    row: 0\r\n");
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, _watchId, elementId);

    [Fact]
    public async Task APlacement_DiscoversItsActions_OrNoDropCouldEverResolve()
    {
        // Arrange & act.
        // Executing an action by id only finds actions its target discovers. A placement that
        // discovered nothing made every drop answer "not available for this item" in the app.
        var path = Write();
        var groups = await _actions.DiscoverAsync(Target(path, TimelineNewPlacement.IdFor(0, 0)), CancellationToken.None);
        var ids = groups.SelectMany(group => group.Actions).Select(action => action.Id).ToList();

        // Assert.
        Assert.Contains(TimelineContextActionProvider.AddElementActionId, ids);
        Assert.Contains(TimelineContextActionProvider.AddMomentActionId, ids);
        Assert.Contains(TimelineContextActionProvider.ConnectActionId, ids);
    }

    [Fact]
    public async Task RemovingARelationFreeElement_HappensInTheExecute_NotInACommitNobodyCalls()
    {
        // Arrange.
        // Execute answering Completed without dispatching is an action that did nothing: the
        // commit leg only runs after a dialog, and a relation-free removal has none.
        var path = Write();

        // Act.
        var result = await _actions.ExecuteAsync(Target(path, "lone"), TimelineContextActionProvider.RemoveActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.Empty(_store.GetOrLoad(path).Model.Elements);
    }

    [Fact]
    public async Task RemovingAnEnd_HappensInTheExecute()
    {
        // Arrange & act.
        var path = Write();
        var result = await _actions.ExecuteAsync(Target(path, "lone"), TimelineContextActionProvider.RemoveEndActionId, CancellationToken.None);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        Assert.False(_store.GetOrLoad(path).Model.Elements.Single().IsPeriod);
    }
}
