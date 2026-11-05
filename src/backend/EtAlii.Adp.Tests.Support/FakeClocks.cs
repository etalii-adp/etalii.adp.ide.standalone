using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Tests.Support;

/// <summary>What a test does with a <see cref="FakeTimeProvider"/> that the type does not do for it.</summary>
public static class FakeClocks
{
    /// <summary>
    /// Advances <paramref name="clock"/> a <paramref name="step"/> at a time until
    /// <paramref name="task"/> has finished, giving its continuations a turn between steps: for work
    /// that waits on the clock more than once, where each wait is only made after the last one ended.
    /// </summary>
    /// <remarks>
    /// Bounded by steps rather than by a deadline, so a task that never finishes fails the test
    /// instead of hanging it; real time spent is only the turns given to continuations.
    /// </remarks>
    public static async Task DriveAsync(this FakeTimeProvider clock, Task task, TimeSpan step, int steps = 100)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(task);

        for (var taken = 0; !task.IsCompleted; taken++)
        {
            if (taken == steps)
            {
                throw new TimeoutException($"Still running after the clock moved {steps} steps of {step}.");
            }

            clock.Advance(step);
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(5), TestContext.Current.CancellationToken));
        }

        await task;
    }
}
