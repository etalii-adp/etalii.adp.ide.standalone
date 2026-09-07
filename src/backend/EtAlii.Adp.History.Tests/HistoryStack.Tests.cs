using Xunit;

namespace EtAlii.Adp.History.Tests;

public class HistoryStackTests
{

    private static (HistoryStack Stack, HistoryStackRecordingDispatcher Dispatcher) CreateStack(int capacity = HistoryStack.DefaultCapacity)
    {
        var dispatcher = new HistoryStackRecordingDispatcher();
        return (new HistoryStack(dispatcher, capacity), dispatcher);
    }

    // ---- construction -----------------------------------------------------------------

    [Fact]
    public void Constructor_WithNullDispatcher_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => new HistoryStack(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithANonPositiveCapacity_Throws(int capacity)
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoryStack(new HistoryStackRecordingDispatcher(), capacity));
    }

    [Fact]
    public void ANewStack_HasNothingToUndoOrRedo()
    {
        // Arrange and act.
        var (stack, _) = CreateStack();
        using var guard = stack;

        // Assert.
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.UndoCount);
        Assert.Equal(0, stack.RedoCount);
    }

    // ---- execute ----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RunsTheCommandAndRecordsIt()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        // Act.
        var result = await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal("a", dispatcher.Value);
        Assert.True(stack.CanUndo);
        Assert.Equal(1, stack.UndoCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheHandlerReportsNoInverse_RunsButRecordsNothing()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        // Act.
        // The fallback branch of HistoryStackRecordingDispatcher returns Success() with no inverse.
        var result = await stack.ExecuteAsync(new HistoryStackUnrecordedCommand(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Single(dispatcher.Dispatched);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCommandFails_RecordsNothing()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;

        // Act.
        var result = await stack.ExecuteAsync(new HistoryStackFailingCommand("nope"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("nope", result.Error);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task ExecuteAsync_WithANullCommand_Throws()
    {
        // Arrange and act.
        var (stack, _) = CreateStack();
        using var guard = stack;

        // Assert.
        await Assert.ThrowsAsync<ArgumentNullException>(() => stack.ExecuteAsync(null!, TestContext.Current.CancellationToken));
    }

    // ---- undo -------------------------------------------------------------------------

    [Fact]
    public async Task UndoAsync_OnAnEmptyStack_FailsAndSaysSo()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        // Act.
        var result = await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("There is nothing to undo.", result.Error);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task UndoAsync_DispatchesTheInverseAndMovesTheEntryToRedo()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);

        // Act.
        var result = await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal("initial", dispatcher.Value);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public async Task UndoAsync_UnwindsChangesNewestFirst()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("b"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("c"), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("b", dispatcher.Value);

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a", dispatcher.Value);

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("initial", dispatcher.Value);
        Assert.False(stack.CanUndo);
        Assert.Equal(3, stack.RedoCount);
    }

    [Fact]
    public async Task UndoAsync_WhenTheInverseFails_KeepsTheEntrySoUndoCanBeRetried()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);

        dispatcher.FailNextWith = "the file is locked";
        var failed = await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        Assert.False(failed.IsSuccess);
        Assert.Equal("the file is locked", failed.Error);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        // ...and once the obstacle is gone, the same undo works.
        var retried = await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(retried.IsSuccess);
        Assert.Equal("initial", dispatcher.Value);
        Assert.True(stack.CanRedo);
    }

    // ---- redo -------------------------------------------------------------------------

    [Fact]
    public async Task RedoAsync_WithNothingUndone_FailsAndSaysSo()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);

        // Act.
        var result = await stack.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("There is nothing to redo.", result.Error);
    }

    [Fact]
    public async Task RedoAsync_ReappliesTheUndoneChange()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        var result = await stack.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Equal("a", dispatcher.Value);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public async Task UndoThenRedo_CanBeWalkedBackAndForwardRepeatedly()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("b"), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        await stack.UndoAsync(TestContext.Current.CancellationToken);
        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("initial", dispatcher.Value);

        await stack.RedoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a", dispatcher.Value);
        await stack.RedoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("b", dispatcher.Value);

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a", dispatcher.Value);
        Assert.Equal(1, stack.UndoCount);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public async Task RedoAsync_WhenTheReplayFails_KeepsTheEntrySoRedoCanBeRetried()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        dispatcher.FailNextWith = "gone";
        var failed = await stack.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(failed.IsSuccess);
        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public async Task ExecuteAsync_AfterAnUndo_DiscardsTheStaleRedoEntries()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("b"), TestContext.Current.CancellationToken);
        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.True(stack.CanRedo);

        // Act.
        await stack.ExecuteAsync(new HistoryStackSetCommand("c"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public async Task ExecuteAsync_ThatFailsAfterAnUndo_LeavesTheRedoEntriesAlone()
    {
        // Arrange.
        // A rejected attempt changed nothing, so it must not cost the user their redo.
        var (stack, _) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        await stack.ExecuteAsync(new HistoryStackFailingCommand("nope"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    // ---- capacity ---------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PastCapacity_DropsTheOldestEntry()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack(capacity: 2);
        using var guard = stack;

        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("b"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("c"), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        Assert.Equal(2, stack.UndoCount);

        // Only "c" and "b" remain undoable; the step back to "initial" fell off the bottom.
        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("b", dispatcher.Value);
        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a", dispatcher.Value);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public async Task Capacity_IsNeverExceededAcrossManyCommands()
    {
        // Arrange.
        var (stack, _) = CreateStack(capacity: 5);
        using var guard = stack;

        // Act.
        for (var i = 0; i < 50; i++)
        {
            await stack.ExecuteAsync(new HistoryStackSetCommand($"v{i}"), TestContext.Current.CancellationToken);
            Assert.True(stack.UndoCount <= 5);
        }

        // Assert.
        Assert.Equal(5, stack.UndoCount);
    }

    // ---- clear ------------------------------------------------------------------------

    [Fact]
    public async Task Clear_ForgetsBothSidesWithoutTouchingTheState()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        await stack.ExecuteAsync(new HistoryStackSetCommand("b"), TestContext.Current.CancellationToken);
        await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        stack.Clear();

        // Assert.
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal("a", dispatcher.Value);
    }

    // ---- concurrency ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_FromManyCallersAtOnce_IsSerialized()
    {
        // Arrange.
        // Requirement: concurrent callers must not interleave, or the stack could be read or
        // moved half-updated. The dispatcher trips a flag for the duration of each dispatch.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;

        // Arrange, continued.
        var inFlight = 0;
        var overlapDetected = false;
        dispatcher.BeforeEachDispatch = async () =>
        {
            if (Interlocked.Increment(ref inFlight) > 1)
            {
                overlapDetected = true;
            }

        // Arrange, continued.
            await Task.Delay(5, TestContext.Current.CancellationToken);
            Interlocked.Decrement(ref inFlight);
        };

        // Act.
        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => stack.ExecuteAsync(new HistoryStackSetCommand($"v{i}"), TestContext.Current.CancellationToken)));

        // Assert.
        Assert.False(overlapDetected);
        Assert.Equal(20, stack.UndoCount);
    }

    [Fact]
    public async Task UndoAndExecute_RacingEachOther_LeaveConsistentCounts()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;
        for (var i = 0; i < 10; i++)
        {
            await stack.ExecuteAsync(new HistoryStackSetCommand($"seed{i}"), TestContext.Current.CancellationToken);
        }

        // Arrange, continued.
        var operations = Enumerable.Range(0, 10)
            .Select<int, Task>(i => i % 2 == 0
                ? stack.ExecuteAsync(new HistoryStackSetCommand($"more{i}"), TestContext.Current.CancellationToken)
                : stack.UndoAsync(TestContext.Current.CancellationToken));

        // Act.
        await Task.WhenAll(operations);

        // Assert.
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
        // Arrange and act.
        var (stack, _) = CreateStack();
        stack.Dispose();

        // Assert.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stack.UndoAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stack.RedoAsync(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => stack.Clear());
    }

    [Fact]
    public void Dispose_CalledTwice_IsHarmless()
    {
        // Arrange.
        var (stack, _) = CreateStack();

        // Act.
        stack.Dispose();
        stack.Dispose();

        // Assert: the second dispose is the subject - reaching here without throwing is the pass.
    }

    // ---- availability -----------------------------------------------------------------

    [Fact]
    public async Task Availability_AgreesWithTheFourProperties_ThroughExecuteUndoAndRedo()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;

        // Act and assert, step by step.
        void AssertAgrees()
        {
            var availability = stack.Availability;
            Assert.Equal(stack.CanUndo, availability.CanUndo);
            Assert.Equal(stack.CanRedo, availability.CanRedo);
            Assert.Equal(stack.UndoCount, availability.UndoCount);
            Assert.Equal(stack.RedoCount, availability.RedoCount);
        }

        AssertAgrees(); // empty
        Assert.Equal(new HistoryAvailability(false, false, 0, 0), stack.Availability);

        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        AssertAgrees();
        Assert.Equal(new HistoryAvailability(true, false, 1, 0), stack.Availability);

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        AssertAgrees();
        Assert.Equal(new HistoryAvailability(false, true, 0, 1), stack.Availability);

        await stack.RedoAsync(TestContext.Current.CancellationToken);
        AssertAgrees();
        Assert.Equal(new HistoryAvailability(true, false, 1, 0), stack.Availability);
    }

    // ---- the Changed event ------------------------------------------------------------

    [Fact]
    public async Task Changed_FiresOnceEach_OnARecordedExecuteASuccessfulUndoAndASuccessfulRedo()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;
        var changed = 0;
        stack.Changed += (_, _) => changed++;

        // Act and assert, step by step.
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);
        Assert.Equal(1, changed);

        await stack.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, changed);

        await stack.RedoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, changed);
    }

    [Fact]
    public async Task Changed_DoesNotFire_WhenACommandIsRejected()
    {
        // Arrange.
        var (stack, _) = CreateStack();
        using var guard = stack;
        var changed = 0;
        stack.Changed += (_, _) => changed++;

        // Act.
        await stack.ExecuteAsync(new HistoryStackFailingCommand("nope"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Changed_DoesNotFire_WhenACommandSucceedsWithoutAnInverse()
    {
        // Arrange.
        // Nothing landed on the undo stack, so nothing about what can be undone changed.
        var (stack, _) = CreateStack();
        using var guard = stack;
        var changed = 0;
        stack.Changed += (_, _) => changed++;

        // Act.
        await stack.ExecuteAsync(new HistoryStackUnrecordedCommand(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Changed_DoesNotFire_WhenAnUndoFailsToApply()
    {
        // Arrange.
        var (stack, dispatcher) = CreateStack();
        using var guard = stack;
        await stack.ExecuteAsync(new HistoryStackSetCommand("a"), TestContext.Current.CancellationToken);

        // Arrange, continued.
        var changed = 0;
        stack.Changed += (_, _) => changed++;
        // The undo dispatches the recorded inverse; make that dispatch fail.
        dispatcher.FailNextWith = "cannot reverse";

        // Act.
        var undo = await stack.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(undo.IsSuccess);
        Assert.Equal(0, changed);
    }

}
