using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The edits of a knowledge session that are shown and not written yet (FR-088): they wait in the
/// order they were made, a written one leaves the later ones waiting, and a refused one takes every
/// later one back with it - in their order - and moves the generation on, so the writes already
/// queued for them write nothing while an edit made afterwards is written.
/// </summary>
/// <remarks>
/// The session-level account of the same, with a real file and a real history, is
/// <c>KnowledgeEditingTests.ARefusedWrite_TakesTheEditBack_AndEveryLaterEditWithIt</c>.
/// </remarks>
public sealed class PendingEditQueueTests
{
    private static readonly IReadOnlyList<ModelChange> AChange = [new ModelChange.Set("table", new Dictionary<string, object?> { ["name"] = "Cities" })];

    private static readonly ShortGuid First = ShortGuid.NewShortGuid();
    private static readonly ShortGuid Second = ShortGuid.NewShortGuid();
    private static readonly ShortGuid Third = ShortGuid.NewShortGuid();

    private static PendingEditQueue ThreeWaiting(out int generation)
    {
        var queue = new PendingEditQueue();
        generation = queue.Add(First, AChange, ("r1", "p1"));
        Assert.Equal(generation, queue.Add(Second, AChange, null));
        Assert.Equal(generation, queue.Add(Third, AChange, ("r2", "p1")));
        return queue;
    }

    [Fact]
    public void ARefusedWrite_TakesBackEveryLaterEdit_InTheOrderTheyWereMade_AndMovesTheGenerationOn()
    {
        // Arrange.
        var queue = ThreeWaiting(out var generation);
        Assert.True(queue.TryStart(generation));

        // Act.
        var takenBack = queue.Settle(First, generation, written: false);

        // Assert: the later edits go with it, in their order, and nothing is waiting any more.
        Assert.Equal([Second, Third], takenBack);
        Assert.Empty(queue.Pending);
        Assert.Empty(queue.UnwrittenCells());

        // The writes already queued for them find there is nothing to write...
        Assert.False(queue.TryStart(generation));

        // ...while an edit made afterwards is of the next generation, and is written.
        var next = queue.Add(ShortGuid.NewShortGuid(), AChange, null);
        Assert.Equal(generation + 1, next);
        Assert.True(queue.TryStart(next));
    }

    [Fact]
    public void AWrittenEdit_LeavesTheLaterOnesWaiting_InTheOrderTheyWereMade()
    {
        // Arrange.
        var queue = ThreeWaiting(out var generation);
        Assert.True(queue.TryStart(generation));

        // Act.
        var takenBack = queue.Settle(First, generation, written: true);

        // Assert.
        Assert.NotNull(takenBack);
        Assert.Empty(takenBack);
        Assert.Equal([Second, Third], queue.Pending.Select(pending => pending.EditId));
        Assert.Equal(("r2", "p1"), Assert.Single(queue.UnwrittenCells()));
        Assert.True(queue.TryStart(generation));
    }

    [Fact]
    public void AWriteOfAnEditAlreadyTakenBack_SettlesNothing_ButIsNoLongerBeingWritten()
    {
        // Arrange: the first edit is refused, and an edit is made afterwards.
        var queue = ThreeWaiting(out var generation);
        queue.TryStart(generation);
        queue.Settle(First, generation, written: false);
        var later = ShortGuid.NewShortGuid();
        queue.Add(later, AChange, null);
        Assert.True(queue.TryStart(generation + 1));

        // Act: a write of the older generation ends.
        var takenBack = queue.Settle(Second, generation, written: false);

        // Assert: the edit made afterwards is not taken back, and no write is under way.
        Assert.Null(takenBack);
        Assert.Equal([later], queue.Pending.Select(pending => pending.EditId));
        Assert.False(queue.Writing);
    }

    [Fact]
    public void AWrite_IsUnderWayFromItsStartToItsEnd_AndEveryStartIsCounted()
    {
        // Arrange.
        var queue = ThreeWaiting(out var generation);

        // Act and assert.
        Assert.False(queue.Writing);
        queue.TryStart(generation);
        Assert.True(queue.Writing);
        queue.Settle(First, generation, written: true);
        Assert.False(queue.Writing);
        queue.TryStart(generation);
        Assert.Equal(2, queue.WritesStarted);
    }

    [Fact]
    public void OnlyTheCellsOfSetCellEdits_AreUnwritten()
    {
        // Arrange.
        var queue = ThreeWaiting(out _);

        // Act.
        var unwritten = queue.UnwrittenCells();

        // Assert.
        Assert.Equal(new[] { ("r1", "p1"), ("r2", "p1") }, unwritten.Order());
    }

    [Fact]
    public async Task QueuedWrites_RunOneAtATime_InTheOrderTheyWereQueued_EvenAfterOneThrows()
    {
        // Arrange: the first write is held open, and throws when it is let go.
        var queue = new PendingEditQueue();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<int> ran = [];
        queue.Enqueue(async () =>
        {
            await held.Task;
            lock (ran)
            {
                ran.Add(1);
            }

            throw new InvalidOperationException("The disk is full.");
        });
        queue.Enqueue(() =>
        {
            lock (ran)
            {
                ran.Add(2);
            }

            return Task.CompletedTask;
        });

        // Act.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        bool secondRanEarly;
        lock (ran)
        {
            secondRanEarly = ran.Count > 0;
        }

        held.SetResult();
        await queue.Writes.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(secondRanEarly);
        Assert.Equal([1, 2], ran);
    }
}
