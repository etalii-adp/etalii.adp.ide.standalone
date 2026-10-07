using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Task 12: every command edits, every undo gives back the original bytes, and every refusal writes
/// nothing - on the field-service example, through the real store on a real file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bytes, not models.</b> An undo is only right if the file is exactly what it was: a model that
/// compares equal can still hide a reordered key or a lost comment. So each case compares the file's
/// bytes before and after.
/// </para>
/// <para>
/// <b>A refusal is checked twice: on disk AND in the store's cache.</b> An edit made in place on the
/// cached document would leave a refused edit behind in memory for the next save to write, while the
/// file on disk still looked untouched.
/// </para>
/// </remarks>
public sealed class FdgCommandsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.FdgCommandsTests", Guid.NewGuid().ToString("N"));
    private readonly FdgDocumentStore _store = new();
    private readonly FdgTestDispatcher _dispatcher;
    private readonly byte[] _original;

    public FdgCommandsTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(FieldServiceExample.Path, Body);
        _original = File.ReadAllBytes(Body);
        _dispatcher = new FdgTestDispatcher(_store);
    }

    private string Body => Path.Combine(_folder, "field-service.fdg");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    public static TheoryData<string> EveryEdit =>
    [
        "add a UI element", "add a comment", "remove an element", "move", "resize", "resize a comment",
        "connect", "disconnect", "rename", "set a comment's text", "describe an element",
        "clear a connection's description", "rename a connection",
    ];

    private ICommand EditNamed(string name) => name switch
    {
        "add a UI element" => new AddFdgElementCommand(Body, FdgElementTypes.UiElement, 300, 700),
        "add a comment" => new AddFdgElementCommand(Body, FdgElementTypes.Comment, 900, 700),
        "remove an element" => new RemoveFdgElementCommand(Body, "task-row"),
        "move" => new SetFdgPlacementCommand(Body, "planning", 10, 20),
        "resize" => new SetFdgSizeCommand(Body, "planning", 220),
        "resize a comment" => new SetFdgSizeCommand(Body, "note-offline", 300, 120),
        // Tick step shows nothing yet, Shows has no limit into a page, and Shows is not ownership.
        "connect" => new ConnectFdgElementsCommand(Body, FdgConnectionTypes.Shows, "tick-step", "step-list"),
        "disconnect" => new DisconnectFdgConnectionCommand(Body, "c-back-shows-list"),
        "rename" => new RenameFdgElementCommand(Body, "planning", "Plan"),
        "set a comment's text" => new RenameFdgElementCommand(Body, "note-offline", "Offline first.\nAlways."),
        "describe an element" => new SetFdgDescriptionCommand(Body, "step-list", "Every step of the open task."),
        "clear a connection's description" => new SetFdgDescriptionCommand(Body, "c-open-shows-detail", ""),
        "rename a connection" => new RenameFdgConnectionCommand(Body, "c-sync-upload", "invokes"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such edit."),
    };

    /// <summary>The task's guard: every edit, then its undo, gives the original bytes.</summary>
    [Theory]
    [MemberData(nameof(EveryEdit))]
    public async Task EveryEdit_ThenItsUndo_GivesTheOriginalBytes(string edit)
    {
        // Act.
        var result = await _dispatcher.DispatchAsync(EditNamed(edit), TestContext.Current.CancellationToken);

        // Assert: it edited - otherwise the undo proves nothing.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));

        // Act: the undo.
        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> EveryRefusal => new()
    {
        { "connect an Action to an Action", "type check" },
        { "link an element to itself", "type check" },
        { "give a UI element a second parent", "cardinality check" },
        { "give an Action a second Shows", "cardinality check" },
        { "close an ownership loop", "cycle check" },
        { "give a UI element a height", "own height" },
        { "remove an element that is not there", "no longer in this graph" },
        { "rename a connection that is not there", "no longer in this graph" },
    };

    private ICommand RefusalNamed(string name) => name switch
    {
        "connect an Action to an Action" => new ConnectFdgElementsCommand(Body, FdgConnectionTypes.OwnsAction, "open-task", "tick-step"),
        "link an element to itself" => new ConnectFdgElementsCommand(Body, FdgConnectionTypes.UiChild, "planning", "planning"),
        // Task row already has Task list as its parent.
        "give a UI element a second parent" => new ConnectFdgElementsCommand(Body, FdgConnectionTypes.UiChild, "planning", "task-row"),
        // Open task already shows Task detail.
        "give an Action a second Shows" => new ConnectFdgElementsCommand(Body, FdgConnectionTypes.Shows, "open-task", "task-list"),
        // Planning owns Task list, which owns Task row: Task row owning Planning closes the loop.
        "close an ownership loop" => new ConnectFdgElementsCommand(Body, FdgConnectionTypes.UiChild, "task-row", "planning"),
        // The writer sets the width BEFORE it refuses the height - the case the copy exists for.
        "give a UI element a height" => new SetFdgSizeCommand(Body, "planning", 300, 200),
        "remove an element that is not there" => new RemoveFdgElementCommand(Body, "nothing-here"),
        "rename a connection that is not there" => new RenameFdgConnectionCommand(Body, "nothing-here", "x"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such refusal."),
    };

    /// <summary>
    /// The task's guard: every refusal leaves the bytes unchanged - on disk, and in the store's cache,
    /// where an edit made in place would survive a refusal and be written by the next save.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRefusal))]
    public async Task EveryRefusal_WritesNothing_AndSaysWhy(string refusal, string saying)
    {
        // Arrange: the cache is loaded, so an in-place edit would have somewhere to hide.
        var cachedBefore = _store.GetOrLoad(Body).Document.Text;

        // Act.
        var result = await _dispatcher.DispatchAsync(RefusalNamed(refusal), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains(saying, result.Error, StringComparison.Ordinal);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(cachedBefore, _store.GetOrLoad(Body).Document.Text);
    }

    /// <summary>
    /// The task's guard: deleting an element with two connections restores all three on one undo.
    /// </summary>
    [Fact]
    public async Task DeletingAnElementWithTwoConnections_RestoresAllThreeOnUndo()
    {
        // Arrange: Task row has exactly two - its parent link, and the Action it offers.
        var before = Parse();
        Assert.Equal(2, before.Connections.Count(connection => connection.From == "task-row" || connection.To == "task-row"));

        // Act.
        var removed = await _dispatcher.DispatchAsync(new RemoveFdgElementCommand(Body, "task-row"), TestContext.Current.CancellationToken);

        // Assert: all three are gone together.
        Assert.True(removed.IsSuccess, removed.Error);
        var after = Parse();
        Assert.DoesNotContain(after.Elements, element => element.Id == "task-row");
        Assert.DoesNotContain(after.Connections, connection => connection.From == "task-row" || connection.To == "task-row");

        // Act: one undo.
        var undone = await _dispatcher.DispatchAsync(removed.Inverse!, TestContext.Current.CancellationToken);

        // Assert: all three are back, byte for byte.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
        Assert.Equal(2, Parse().Connections.Count(connection => connection.From == "task-row" || connection.To == "task-row"));
    }

    /// <summary>
    /// The task's named sabotage: a connect command that trusts the client. A cycle-closing request is
    /// refused by the backend's own check, whatever the canvas that sent it believed.
    /// </summary>
    [Fact]
    public async Task ACycleClosingLink_IsRefusedByTheBackend_AndNothingIsWritten()
    {
        // Arrange: Planning owns Task list, which owns Task row.
        Assert.True(FdgOwnership.WouldClose(Parse(), "task-row", "planning"));

        // Act.
        var result = await _dispatcher.DispatchAsync(
            new ConnectFdgElementsCommand(Body, FdgConnectionTypes.UiChild, "task-row", "planning"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cycle check", result.Error, StringComparison.Ordinal);
        Assert.Empty(FdgOwnership.CyclesIn(Parse()));
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    /// <summary>A drag reaches the document through the session and is one undo away, like every other edit.</summary>
    [Fact]
    public async Task AMoveThroughTheSession_IsWritten_AndOneUndoAway()
    {
        // Arrange.
        using var historyStacks = new HistoryStackStore(_dispatcher);
        var history = historyStacks.Get(_folder);
        await using var session = new FdgSession(Body, _store, new FdgElementMapper(), history);

        // Act.
        var error = await session.MoveElementToAsync("planning", 10, 20, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", error);
        var planning = Parse().Elements.Single(element => element.Id == "planning");
        Assert.Equal((10d, 20d), (planning.X, planning.Y));

        // Act: undo, through the history the session used.
        var undone = await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A document that could not be read is never written: its emptiness is not the document, and
    /// writing it would replace the only copy on disk - the loss causal loop measured.
    /// </summary>
    [Fact]
    public async Task ADocumentThatCouldNotBeRead_IsNeverWritten()
    {
        // Arrange: another program holds the file, so the first open cannot read it.
        var store = new FdgDocumentStore();
        FdgDocumentEntry entry;
        await using (new FileStream(Body, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            entry = store.GetOrLoad(Body);
        }

        Assert.False(entry.IsUsable);
        Assert.Empty(entry.Model.Elements);

        // Act: an edit, and a save straight through the store.
        var edited = await new FdgTestDispatcher(store).DispatchAsync(
            new RenameFdgElementCommand(Body, "planning", "Plan"),
            TestContext.Current.CancellationToken);
        var saved = store.Save(Body, LineDocument.Parse("functional-decomposition-graph: 1\r\nelements: []\r\nconnections: []\r\n"));

        // Assert: both refused, and the file is still the real diagram.
        Assert.False(edited.IsSuccess);
        Assert.True(saved.Failed);
        Assert.Equal(_original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    private FdgModel Parse() => FdgParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));
}
