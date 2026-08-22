using EtAlii.Adp.Backend.History;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class HistoryStackTests
{
    /// <summary>A command that "sets" a value; its inverse sets the previous one back.</summary>
    private sealed record SetCommand(string Value) : ICommand;

    private sealed record FailingCommand(string Error) : ICommand;

    /// <summary>
    /// Applies <see cref="SetCommand"/>s to an in-memory value, recording the order every
    /// command was seen in so tests can assert on what actually ran, not just on counts.
    /// </summary>
    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        private readonly List<ICommand> _dispatched = [];

        public IReadOnlyList<ICommand> Dispatched => _dispatched;

        public string Value { get; private set; } = "initial";

        /// <summary>Set to have the next dispatch of a <see cref="SetCommand"/> fail.</summary>
        public string? FailNextWith { get; set; }

        public Func<Task>? BeforeEachDispatch { get; set; }

        public async Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default)
        {
            if (BeforeEachDispatch is not null)
            {
                await BeforeEachDispatch();
            }

            _dispatched.Add(command);

            if (FailNextWith is { } error)
            {
                FailNextWith = null;
                return CommandResult.Failure(error);
            }

            return command switch
            {
                SetCommand set => Apply(set),
                FailingCommand failing => CommandResult.Failure(failing.Error),
                _ => CommandResult.Success(),
            };
        }

        private CommandResult Apply(SetCommand command)
        {
            var previous = Value;
            Value = command.Value;
            return CommandResult.Success(new SetCommand(previous));
        }
    }

    private static (HistoryStack Stack, RecordingDispatcher Dispatcher) CreateStack(int capacity = HistoryStack.DefaultCapacity)
    {
        var dispatcher = new RecordingDispatcher();
        return (new HistoryStack(dispatcher, capacity), dispatcher);
    }

    // ---- construction -----------------------------------------------------------------

    [Fact]
    public void Constructor_WithNullDispatcher_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HistoryStack(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithANonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoryStack(new RecordingDispatcher(), capacity));
    }

    [Fact]
    public void ANewStack_HasNothingToUndoOrRedo()
    {
        var (stack, _) = CreateStack();
        using var guard = stack;

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
    }

    // ---- execute ----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RunsTheCommandAndRecordsIt()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        var result = await stack.ExecuteAsync(new SetCommand("a"));

        Assert.True(result.IsSuccess);
        Assert.Equal("a", dispatcher.Value);
        Assert.True(stack.CanUndo);
        Assert.Equal(1, stack.UndoCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheHandlerReportsNoInverse_RunsButRecordsNothing()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        // The fallback branch of RecordingDispatcher returns Success() with no inverse.
        var result = await stack.ExecuteAsync(new UnrecordedCommand());

        Assert.True(result.IsSuccess);
        Assert.Single(dispatcher.Dispatched);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCommandFails_RecordsNothing()
    {
        var (stack, _) = CreateStack();
        using var guard = stack;

        var result = await stack.ExecuteAsync(new FailingCommand("nope"));

        Assert.False(result.IsSuccess);
        Assert.Equal("nope", result.Error);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ExecuteAsync_WithANullCommand_Throws()
    {
        var (stack, _) = CreateStack();
        using var guard = stack;

        await Assert.ThrowsAsync<ArgumentNullException>(() => stack.ExecuteAsync(null!));
    }

    // ---- undo -------------------------------------------------------------------------

    [Fact]
    public async Task UndoAsync_OnAnEmptyStack_FailsAndSaysSo()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        var result = await stack.UndoAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("There is nothing to undo.", result.Error);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task UndoAsync_DispatchesTheInverseAndMovesTheEntryToRedo()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));

        var result = await stack.UndoAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("initial", dispatcher.Value);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public async Task UndoAsync_UnwindsChangesNewestFirst()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.ExecuteAsync(new SetCommand("b"));
        await stack.ExecuteAsync(new SetCommand("c"));

        await stack.UndoAsync();
        Assert.Equal("b", dispatcher.Value);

        await stack.UndoAsync();
        Assert.Equal("a", dispatcher.Value);

        await stack.UndoAsync();
        Assert.Equal("initial", dispatcher.Value);
        Assert.False(stack.CanUndo);
        Assert.Equal(3, stack.RedoCount);
    }

    [Fact]
    public async Task UndoAsync_WhenTheInverseFails_KeepsTheEntrySoUndoCanBeRetried()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));

        dispatcher.FailNextWith = "the file is locked";
        var failed = await stack.UndoAsync();

        Assert.False(failed.IsSuccess);
        Assert.Equal("the file is locked", failed.Error);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        // ...and once the obstacle is gone, the same undo works.
        var retried = await stack.UndoAsync();
        Assert.True(retried.IsSuccess);
        Assert.Equal("initial", dispatcher.Value);
        Assert.True(stack.CanRedo);
    }

    // ---- redo -------------------------------------------------------------------------

    [Fact]
    public async Task RedoAsync_WithNothingUndone_FailsAndSaysSo()
    {
        var (stack, _) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));

        var result = await stack.RedoAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("There is nothing to redo.", result.Error);
    }

    [Fact]
    public async Task RedoAsync_ReappliesTheUndoneChange()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.UndoAsync();

        var result = await stack.RedoAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("a", dispatcher.Value);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public async Task UndoThenRedo_CanBeWalkedBackAndForwardRepeatedly()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.ExecuteAsync(new SetCommand("b"));

        await stack.UndoAsync();
        await stack.UndoAsync();
        Assert.Equal("initial", dispatcher.Value);

        await stack.RedoAsync();
        Assert.Equal("a", dispatcher.Value);
        await stack.RedoAsync();
        Assert.Equal("b", dispatcher.Value);

        await stack.UndoAsync();
        Assert.Equal("a", dispatcher.Value);
        Assert.Equal(1, stack.UndoCount);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public async Task RedoAsync_WhenTheReplayFails_KeepsTheEntrySoRedoCanBeRetried()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.UndoAsync();

        dispatcher.FailNextWith = "gone";
        var failed = await stack.RedoAsync();

        Assert.False(failed.IsSuccess);
        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public async Task ExecuteAsync_AfterAnUndo_DiscardsTheStaleRedoEntries()
    {
        var (stack, _) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.ExecuteAsync(new SetCommand("b"));
        await stack.UndoAsync();
        Assert.True(stack.CanRedo);

        await stack.ExecuteAsync(new SetCommand("c"));

        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public async Task ExecuteAsync_ThatFailsAfterAnUndo_LeavesTheRedoEntriesAlone()
    {
        // A rejected attempt changed nothing, so it must not cost the user their redo.
        var (stack, _) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.UndoAsync();

        await stack.ExecuteAsync(new FailingCommand("nope"));

        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    // ---- capacity ---------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PastCapacity_DropsTheOldestEntry()
    {
        var (stack, dispatcher) = CreateStack(capacity: 2);
        using var guard = stack;

        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.ExecuteAsync(new SetCommand("b"));
        await stack.ExecuteAsync(new SetCommand("c"));

        Assert.Equal(2, stack.UndoCount);

        // Only "c" and "b" remain undoable; the step back to "initial" fell off the bottom.
        await stack.UndoAsync();
        Assert.Equal("b", dispatcher.Value);
        await stack.UndoAsync();
        Assert.Equal("a", dispatcher.Value);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task Capacity_IsNeverExceededAcrossManyCommands()
    {
        var (stack, _) = CreateStack(capacity: 5);
        using var guard = stack;

        for (var i = 0; i < 50; i++)
        {
            await stack.ExecuteAsync(new SetCommand($"v{i}"));
            Assert.True(stack.UndoCount <= 5);
        }

        Assert.Equal(5, stack.UndoCount);
    }

    // ---- clear ------------------------------------------------------------------------

    [Fact]
    public async Task Clear_ForgetsBothSidesWithoutTouchingTheState()
    {
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new SetCommand("a"));
        await stack.ExecuteAsync(new SetCommand("b"));
        await stack.UndoAsync();

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal("a", dispatcher.Value);
    }

    // ---- concurrency ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_FromManyCallersAtOnce_IsSerialized()
    {
        // Requirement: concurrent callers must not interleave, or the stack could be read or
        // moved half-updated. The dispatcher trips a flag for the duration of each dispatch.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        var inFlight = 0;
        var overlapDetected = false;
        dispatcher.BeforeEachDispatch = async () =>
        {
            if (Interlocked.Increment(ref inFlight) > 1)
            {
                overlapDetected = true;
            }

            await Task.Delay(5);
            Interlocked.Decrement(ref inFlight);
        };

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => stack.ExecuteAsync(new SetCommand($"v{i}"))));

        Assert.False(overlapDetected);
        Assert.Equal(20, stack.UndoCount);
    }

    [Fact]
    public async Task UndoAndExecute_RacingEachOther_LeaveConsistentCounts()
    {
        var (stack, _) = CreateStack();
        using var guard = stack;
        for (var i = 0; i < 10; i++)
        {
            await stack.ExecuteAsync(new SetCommand($"seed{i}"));
        }

        var operations = Enumerable.Range(0, 10)
            .Select<int, Task>(i => i % 2 == 0
                ? stack.ExecuteAsync(new SetCommand($"more{i}"))
                : stack.UndoAsync());

        await Task.WhenAll(operations);

        // 10 seeded + 5 more executed = 15 entries at most. Each execute clears the redo side, so
        // the total can shrink below that - but an execute also adds one as it clears, so the
        // 10 seeded entries can never all disappear. Anything outside that band means an entry
        // was lost or double-counted by the interleaving.
        Assert.InRange(stack.UndoCount + stack.RedoCount, 10, 15);
    }

    // ---- disposal ---------------------------------------------------------------------

    [Fact]
    public async Task Operations_AfterDispose_Throw()
    {
        var (stack, _) = CreateStack();
        stack.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => stack.ExecuteAsync(new SetCommand("a")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stack.UndoAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stack.RedoAsync());
        Assert.Throws<ObjectDisposedException>(() => stack.Clear());
    }

    [Fact]
    public void Dispose_CalledTwice_IsHarmless()
    {
        var (stack, _) = CreateStack();

        stack.Dispose();
        stack.Dispose();
    }

    private sealed record UnrecordedCommand : ICommand;
}
