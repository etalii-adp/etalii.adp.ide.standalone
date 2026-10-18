using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// The tasks an owner starts and does not await where it starts them (code-structure FR-070):
/// recorded while they run, logged when they fail, and stopped and waited for when the owner closes.
/// </summary>
[Collection(LogCapture.Collection)]
public sealed class RunningTasksTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ATaskThatReturns_IsForgotten()
    {
        // Arrange.
        using var running = new RunningTasks();
        var letGo = new TaskCompletionSource();

        // Act.
        running.Start(async _ => await letGo.Task);
        var whileRunning = running.Count;
        letGo.SetResult();

        // Assert.
        Assert.Equal(1, whileRunning);
        Assert.Equal(0, running.Count);
        await running.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_CancelsTheTasksStillRunning_AndReturnsOnlyOnceTheyHave()
    {
        // Arrange: a task that, once cancelled, finishes only when it is let go.
        using var running = new RunningTasks();
        var finish = new TaskCompletionSource();
        var cancelled = false;
        running.Start(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                await finish.Task;
                throw;
            }
        });

        // Act.
        var stopping = running.StopAsync(TestContext.Current.CancellationToken);

        // Assert: still stopping while the task finishes, stopped once it has.
        var first = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.False(first == stopping, "StopAsync returned while a task it cancelled was still running.");
        finish.SetResult();
        await stopping.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(cancelled);
        Assert.Equal(0, running.Count);
    }

    [Fact]
    public async Task ATaskThatFails_IsLogged_AndForgotten()
    {
        // Arrange.
        using var logs = LogCapture.Start();
        using var running = new RunningTasks();

        // Act.
        running.Start(_ => Task.FromException(new InvalidOperationException("the work broke")));
        await running.StopAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Contains(logs.Errors, error => error.Contains("A task started and left running failed", StringComparison.Ordinal));
        Assert.Equal(0, running.Count);
    }

    [Fact]
    public async Task WorkStartedAfterStopAsync_IsHandedACancelledToken()
    {
        // Arrange.
        using var running = new RunningTasks();
        await running.StopAsync(TestContext.Current.CancellationToken);
        var handed = CancellationToken.None;

        // Act.
        running.Start(cancellationToken =>
        {
            handed = cancellationToken;
            return Task.CompletedTask;
        });

        // Assert.
        Assert.True(handed.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposingTwice_ThenStopping_DoesNotThrow()
    {
        // Arrange.
        var running = new RunningTasks();

        // Act.
        running.Dispose();
        running.Dispose();
        await running.StopAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(0, running.Count);
    }
}
