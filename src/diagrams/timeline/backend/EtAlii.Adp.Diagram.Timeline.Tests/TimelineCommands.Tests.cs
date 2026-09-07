using EtAlii.Adp.Common;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Every command through the real history stack: applied, undone, and - where a guard exists -
/// refused. The recurring assertion is byte identity after undo, because "the file is exactly
/// as it was" is the promise the whole command layer is built around.
/// </summary>
public class TimelineCommandsTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-commands-" + Guid.NewGuid().ToString("N"));

    private readonly TimelineDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;

    private IHistoryStack History => _historyStacks.Get(_workspace);

    public TimelineCommandsTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new TimelineTestDispatcher(_store));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Timeline = """
        timeline: 1
        # A comment the commands must never disturb.
        elements:
          - id: aaa
            label: Period
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: bbb
            label: Moment
            begin: 2026-02-16T14:00:00
            row: 2
        connections:
          - id: ccc
            from: aaa
            to: bbb
            label: gates
        """;

    private string Write(string content = Timeline)
    {
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    [Fact]
    public async Task AddThenUndo_IsByteIdentical()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(new AddTimelineElementCommand(path, "new00001", "Added", "2026-03-01", "2026-03-15", 4), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddingAMoment_WritesNoEndKey()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new AddTimelineElementCommand(path, "mmm00001", "Milestone", "2026-06-01", null, 1), TestContext.Current.CancellationToken);

        // Assert.
        var model = _store.GetOrLoad(path).Model;
        Assert.False(model.Elements.Single(element => element.Id == "mmm00001").IsPeriod);
    }

    [Fact]
    public async Task AddingAnElementBornInverted_IsRefused()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new AddTimelineElementCommand(path, "inv00001", "Backwards", "2026-06-01", "2026-05-01", 0), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot end before it begins", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingADuplicateId_IsRefused()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new AddTimelineElementCommand(path, "aaa", "Duplicate", "2026-06-01", null, 0), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RemoveTakesTheConnections_AndUndoPutsEverythingBackByteForByte()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(new RemoveTimelineElementCommand(path, "aaa"), TestContext.Current.CancellationToken);
        var afterRemove = _store.GetOrLoad(path).Model;
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(afterRemove.Connections);
        Assert.Single(afterRemove.Elements);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveUndoRedo_RemovesItAgain()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new RemoveTimelineElementCommand(path, "aaa"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);
        var redo = await History.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redo.IsSuccess, redo.Error);
        Assert.DoesNotContain("aaa", _store.GetOrLoad(path).Model.Elements.Select(element => element.Id));
    }

    [Fact]
    public async Task RemovingTheLastElementOfAFileWithNoTrailingNewline_UndoesByteForByte()
    {
        // Arrange.
        // The hardest byte case: the removed block carried the file's missing terminator.
        var unterminated = "timeline: 1\r\nelements:\r\n  - id: last\r\n    begin: 2026-01-01\r\n    row: 0";
        var path = Write(unterminated);

        // Act.
        await History.ExecuteAsync(new RemoveTimelineElementCommand(path, "last"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(unterminated, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenameChangesOneLine_LeavesTheId_AndUndoes()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await History.ExecuteAsync(new RenameTimelineElementCommand(path, "aaa", "Renamed"), TestContext.Current.CancellationToken);
        var renamed = _store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "aaa");
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Renamed", renamed.Label);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APlacementThatWouldInvert_IsRefusedByTheHandler()
    {
        // Arrange.
        // The server-side half of Requirement 3.4 - deliberately not going through any client
        // clamp, because this is the guard that holds when no client is well-behaved.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new SetTimelinePlacementCommand(path, "aaa", "2026-03-01", "2026-02-01", 0, "Edited"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot end before it begins", result.Error, StringComparison.Ordinal);
        Assert.Contains("begin: 2026-01-05", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlacementNeverGivesAMomentAnEnd()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new SetTimelinePlacementCommand(path, "bbb", "2026-05-01T09:00:00", "2026-05-02T09:00:00", 2, "Moved"), TestContext.Current.CancellationToken);

        // Assert.
        // The end is ignored rather than written: giving a moment an end is the context menu's
        // action (Requirement 7.5), never a drag's side effect.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "bbb").IsPeriod);
    }

    [Fact]
    public async Task ConnectThenUndo_IsByteIdentical()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(new ConnectTimelineElementsCommand(path, "ddd", "bbb", "aaa", "back"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASelfConnection_IsRefusedWithTheReason()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new ConnectTimelineElementsCommand(path, "ddd", "aaa", "aaa", ""), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be connected to itself", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondConnectionBetweenTheSamePair_IsPermitted()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new ConnectTimelineElementsCommand(path, "ddd", "aaa", "bbb", "again"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, _store.GetOrLoad(path).Model.Connections.Count);
    }

    [Fact]
    public async Task AConnectionToAnAbsentElement_IsRefused()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(new ConnectTimelineElementsCommand(path, "ddd", "aaa", "ghost", ""), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DisconnectThenUndo_BringsTheConnectionBack()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new DisconnectTimelineConnectionCommand(path, "ccc"), TestContext.Current.CancellationToken);
        var afterDisconnect = _store.GetOrLoad(path).Model.Connections.Count;
        await History.UndoAsync(TestContext.Current.CancellationToken);
        var restored = TimelineEdits.ConnectionOf(_store.GetOrLoad(path).Model, "ccc");

        // Assert.
        Assert.Equal(0, afterDisconnect);
        Assert.NotNull(restored);
        Assert.Equal("gates", restored.Label);
    }

    [Fact]
    public async Task RelabelThenUndo_IsByteIdentical()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await History.ExecuteAsync(new RelabelTimelineConnectionCommand(path, "ccc", "renamed"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoCommandTouchesTheComment()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new AddTimelineElementCommand(path, "new00001", "Added", "2026-03-01", null, 4), TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new RenameTimelineElementCommand(path, "aaa", "Renamed"), TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new ConnectTimelineElementsCommand(path, "ddd", "new00001", "aaa", ""), TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new RemoveTimelineElementCommand(path, "new00001"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("# A comment the commands must never disturb.", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditOnAnUnparseableFile_IsRefusedAndTheFileUntouched()
    {
        // Arrange.
        // Requirement 2.4: a broken file is never made worse.
        var broken = "elements: [\n  - id: a\n";
        var path = Write(broken);

        // Act.
        var result = await History.ExecuteAsync(new RenameTimelineElementCommand(path, "a", "Renamed"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(broken, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
}
