using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Every command through the real history stack: applied, undone, and - where a guard exists -
/// refused. The recurring assertion is byte identity after undo, because "the file is exactly
/// as it was" is the promise the whole command layer is built around.
/// </summary>
public class DependencyGraphCommandsTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-dgr-commands-" + Guid.NewGuid().ToString("N"));

    private readonly DependencyGraphDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;

    private IHistoryStack History => _historyStacks.Get(_workspace);

    public DependencyGraphCommandsTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new DependencyGraphTestDispatcher(_store));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Graph = """
        dependencies: 1
        # A comment the commands must never disturb.
        elements:
          - id: aaa
            label: API gateway
            x: 240
            row: 0
          - id: bbb
            label: Identity service
            x: 480
            row: 2
        relations:
          - id: ccc
            from: aaa
            to: bbb
            label: verifies tokens with
        """;

    private string Write(string content = Graph)
    {
        var path = IoPath.Combine(_workspace, "services.dgr");
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
        var result = await History.ExecuteAsync(
            new AddDependencyGraphElementCommand(path, "new00001", "Added", 720, 4), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddingANodeAtZeroOrANegativeCoordinate_IsAccepted()
    {
        // Arrange.
        // The timeline refused an element with no begin and one born ending before it began.
        // Both guards went with the dates: every coordinate names a place, so there is nothing
        // left here to refuse but a missing id.
        var path = Write();

        // Act.
        var atOrigin = await History.ExecuteAsync(
            new AddDependencyGraphElementCommand(path, "zero0001", "At the origin", 0, 0), TestContext.Current.CancellationToken);
        var toTheLeft = await History.ExecuteAsync(
            new AddDependencyGraphElementCommand(path, "neg00001", "To the left", -400, -2), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(atOrigin.IsSuccess, atOrigin.Error);
        Assert.True(toTheLeft.IsSuccess, toTheLeft.Error);
        var model = _store.GetOrLoad(path).Model;
        Assert.Equal(0d, model.Elements.Single(element => element.Id == "zero0001").X);
        Assert.Equal(-400d, model.Elements.Single(element => element.Id == "neg00001").X);
    }

    [Fact]
    public async Task AddingANodeWithNoId_IsRefused()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(
            new AddDependencyGraphElementCommand(path, "", "Anonymous", 0, 0), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("needs an id", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingADuplicateId_IsRefused()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(
            new AddDependencyGraphElementCommand(path, "aaa", "Duplicate", 600, 0), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RemoveTakesTheRelations_AndUndoPutsEverythingBackByteForByte()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(
            new RemoveDependencyGraphElementCommand(path, "aaa"), TestContext.Current.CancellationToken);
        var afterRemove = _store.GetOrLoad(path).Model;
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(afterRemove.Relations);
        Assert.Single(afterRemove.Elements);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveUndoRedo_RemovesItAgain()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new RemoveDependencyGraphElementCommand(path, "aaa"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);
        var redo = await History.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redo.IsSuccess, redo.Error);
        Assert.DoesNotContain("aaa", _store.GetOrLoad(path).Model.Elements.Select(element => element.Id));
    }

    [Fact]
    public async Task RemovingTheLastNodeOfAFileWithNoTrailingNewline_UndoesByteForByte()
    {
        // Arrange.
        // The hardest byte case: the removed block carried the file's missing terminator.
        var unterminated = "dependencies: 1\r\nelements:\r\n  - id: last\r\n    x: 100\r\n    row: 0";
        var path = Write(unterminated);

        // Act.
        await History.ExecuteAsync(new RemoveDependencyGraphElementCommand(path, "last"), TestContext.Current.CancellationToken);
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
        await History.ExecuteAsync(new RenameDependencyGraphElementCommand(path, "aaa", "Renamed"), TestContext.Current.CancellationToken);
        var renamed = _store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "aaa");
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Renamed", renamed.Label);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APlacementMovesTheNode_AndUndoesByteForByte()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(
            new SetDependencyGraphPlacementCommand(path, "aaa", 812.5, 6, "Moved"), TestContext.Current.CancellationToken);
        var moved = _store.GetOrLoad(path).Model.Elements.Single(element => element.Id == "aaa");
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(812.5d, moved.X);
        Assert.Equal(6, moved.Row);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APlacementOnAnAbsentNode_IsRefusedAndTheFileUntouched()
    {
        // Arrange.
        // Undo and redo dispatch the same instance again later, so preconditions are checked
        // against the document as it is now.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(
            new SetDependencyGraphPlacementCommand(path, "ghost", 100, 0, "Moved"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectThenUndo_IsByteIdentical()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(
            new ConnectDependencyGraphElementsCommand(path, "ddd", "bbb", "aaa", "back"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectWritesTheDependencyInTheDirectionItWasGiven()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(
            new ConnectDependencyGraphElementsCommand(path, "ddd", "bbb", "aaa", ""), TestContext.Current.CancellationToken);

        // Assert.
        // A connect that normalised its ends - sorting them, or always writing the selected node
        // first - would reverse dependencies while leaving a graph that still draws.
        var added = _store.GetOrLoad(path).Model.Relations.Single(relation => relation.Id == "ddd");
        Assert.Equal("bbb", added.From);
        Assert.Equal("aaa", added.To);
    }

    [Fact]
    public async Task ASelfDependency_IsRefusedWithTheReason()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(
            new ConnectDependencyGraphElementsCommand(path, "ddd", "aaa", "aaa", ""), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot depend on itself", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondDependencyBetweenTheSamePair_IsPermitted()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(
            new ConnectDependencyGraphElementsCommand(path, "ddd", "aaa", "bbb", "again"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, _store.GetOrLoad(path).Model.Relations.Count);
    }

    [Fact]
    public async Task ADependencyOnAnAbsentNode_IsRefused()
    {
        // Arrange.
        var path = Write();

        // Act.
        var result = await History.ExecuteAsync(
            new ConnectDependencyGraphElementsCommand(path, "ddd", "aaa", "ghost", ""), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DisconnectThenUndo_BringsTheDependencyBackTheRightWayRound()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new DisconnectDependencyGraphRelationCommand(path, "ccc"), TestContext.Current.CancellationToken);
        var afterDisconnect = _store.GetOrLoad(path).Model.Relations.Count;
        await History.UndoAsync(TestContext.Current.CancellationToken);
        var restored = DependencyGraphEdits.RelationOf(_store.GetOrLoad(path).Model, "ccc");

        // Assert.
        Assert.Equal(0, afterDisconnect);
        Assert.NotNull(restored);
        Assert.Equal("aaa", restored.From);
        Assert.Equal("bbb", restored.To);
        Assert.Equal("verifies tokens with", restored.Label);
    }

    [Fact]
    public async Task RelabelThenUndo_IsByteIdentical()
    {
        // Arrange.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await History.ExecuteAsync(new RelabelDependencyGraphRelationCommand(path, "ccc", "renamed"), TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddConnected_CreatesBothAsOneHistoryEntry()
    {
        // Arrange.
        // A relation dragged onto empty canvas: one gesture, one undo, both things gone.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await History.ExecuteAsync(
            new AddConnectedDependencyGraphElementCommand(path, "aaa", "new00001", "rel00001", 900, 3),
            TestContext.Current.CancellationToken);
        var created = _store.GetOrLoad(path).Model;
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, created.Elements.Count);
        var relation = created.Relations.Single(candidate => candidate.Id == "rel00001");
        Assert.Equal("aaa", relation.From);
        Assert.Equal("new00001", relation.To);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddConnectedFromAnIncomingAnchor_MakesTheNewNodeTheDependent()
    {
        // Arrange.
        // Dragged into an existing node rather than out of it: the thing being created is what
        // depends on it, so the relation runs the other way. Getting this backwards draws the
        // same picture with the arrow reversed.
        var path = Write();

        // Act.
        await History.ExecuteAsync(
            new AddConnectedDependencyGraphElementCommand(path, "aaa", "new00002", "rel00002", -200, 1, NewElementIsSource: true),
            TestContext.Current.CancellationToken);

        // Assert.
        var relation = _store.GetOrLoad(path).Model.Relations.Single(candidate => candidate.Id == "rel00002");
        Assert.Equal("new00002", relation.From);
        Assert.Equal("aaa", relation.To);
    }

    [Fact]
    public async Task AddConnectedIntoAGraphWithNoRelationsSection_UndoesByteForByte()
    {
        // Arrange.
        // The stray `relations:` header is the one line that would break undo's byte identity,
        // and this is the path that creates it.
        var bare = "dependencies: 1\r\nelements:\r\n  - id: only\r\n    label: Only node\r\n    x: 0\r\n    row: 0\r\n";
        var path = Write(bare);

        // Act.
        var result = await History.ExecuteAsync(
            new AddConnectedDependencyGraphElementCommand(path, "only", "new00003", "rel00003", 240, 0),
            TestContext.Current.CancellationToken);
        await History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(bare, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoCommandTouchesTheComment()
    {
        // Arrange.
        var path = Write();

        // Act.
        await History.ExecuteAsync(new AddDependencyGraphElementCommand(path, "new00001", "Added", 720, 4), TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new RenameDependencyGraphElementCommand(path, "aaa", "Renamed"), TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new ConnectDependencyGraphElementsCommand(path, "ddd", "new00001", "aaa", ""), TestContext.Current.CancellationToken);
        await History.ExecuteAsync(new RemoveDependencyGraphElementCommand(path, "new00001"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains("# A comment the commands must never disturb.", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditOnAnUnparseableFile_IsRefusedAndTheFileUntouched()
    {
        // Arrange.
        // A broken file is never made worse.
        var broken = "elements: [\n  - id: a\n";
        var path = Write(broken);

        // Act.
        var result = await History.ExecuteAsync(
            new RenameDependencyGraphElementCommand(path, "a", "Renamed"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(broken, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NoCommandCarriesADateShapedField()
    {
        // Assert.
        // The deletion, pinned where it would come back: a fork that reintroduced a begin, an end
        // or a duration would do it on a command record first, and every other date in the module
        // would follow it.
        var commands = typeof(SetDependencyGraphPlacementCommand).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(ICommand).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(commands);
        foreach (var command in commands)
        {
            foreach (var property in command.GetProperties())
            {
                Assert.NotEqual(typeof(DateTimeOffset), property.PropertyType);
                Assert.NotEqual(typeof(DateTime), property.PropertyType);
                Assert.NotEqual(typeof(TimeSpan), property.PropertyType);
                foreach (var word in new[] { "Begin", "End", "Duration", "Instant" })
                {
                    Assert.DoesNotContain(word, property.Name, StringComparison.Ordinal);
                }
            }
        }
    }
}
