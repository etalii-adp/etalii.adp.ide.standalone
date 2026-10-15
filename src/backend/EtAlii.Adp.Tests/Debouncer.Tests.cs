using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// The debouncer every coalescing timer of the backend runs on (code-structure FR-051). A fake
/// clock runs its timers inside <see cref="FakeTimeProvider.Advance"/>, so each test says exactly
/// when a window passes, and a callback either ran by the end of that call or never will.
/// </summary>
public sealed class DebouncerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan JustShort = Window - TimeSpan.FromMilliseconds(1);

    private readonly FakeTimeProvider _clock = new();
    private readonly List<string> _elapsed = [];

    private Debouncer<string> Debouncer(IEqualityComparer<string>? comparer = null) =>
        new(Window, _elapsed.Add, _clock, comparer);

    [Fact]
    public void AScheduledKey_ElapsesOnce_WhenItsWindowPasses()
    {
        // Arrange.
        using var debouncer = Debouncer();
        debouncer.Schedule("a");

        // Act and assert, step by step.
        _clock.Advance(JustShort);
        Assert.Empty(_elapsed);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["a"], _elapsed);

        _clock.Advance(Window * 10);
        Assert.Equal(["a"], _elapsed);
    }

    [Fact]
    public void SchedulingAgain_StartsTheWindowOver()
    {
        // Arrange.
        using var debouncer = Debouncer();

        // Act: five schedules, each before the one before it would have elapsed.
        for (var i = 0; i < 5; i++)
        {
            debouncer.Schedule("a");
            _clock.Advance(JustShort);
        }

        // Assert, step by step.
        Assert.Empty(_elapsed);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["a"], _elapsed);
    }

    [Fact]
    public void Keys_HaveWindowsOfTheirOwn()
    {
        // Arrange.
        using var debouncer = Debouncer();
        debouncer.Schedule("a");
        _clock.Advance(JustShort);

        // Act: b's schedule does not start a's window over.
        debouncer.Schedule("b");
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert, step by step.
        Assert.Equal(["a"], _elapsed);
        _clock.Advance(Window);
        Assert.Equal(["a", "b"], _elapsed);
    }

    [Fact]
    public void TheComparer_DecidesWhichSchedulesAreOneKey()
    {
        // Arrange.
        using var debouncer = Debouncer(StringComparer.OrdinalIgnoreCase);

        // Act.
        debouncer.Schedule("C:\\Project");
        debouncer.Schedule("c:\\project");
        _clock.Advance(Window);

        // Assert: one key, so one callback, for the schedule that came last.
        Assert.Equal(["c:\\project"], _elapsed);
    }

    [Fact]
    public void Cancel_DropsThePendingCallback_AndSaysWhetherThereWasOne()
    {
        // Arrange.
        using var debouncer = Debouncer();
        debouncer.Schedule("a");

        // Act.
        var first = debouncer.Cancel("a");
        var second = debouncer.Cancel("a");
        _clock.Advance(Window);

        // Assert.
        Assert.True(first);
        Assert.False(second);
        Assert.Empty(_elapsed);
    }

    [Fact]
    public void Dispose_DropsEveryPendingCallback_AndLaterSchedulesDoNothing()
    {
        // Arrange.
        var debouncer = Debouncer();
        debouncer.Schedule("a");
        debouncer.Schedule("b");

        // Act.
        debouncer.Dispose();
        debouncer.Schedule("c");
        _clock.Advance(Window);

        // Assert.
        Assert.Empty(_elapsed);
    }

    [Fact]
    public void TheCallback_MaySchedule_ItsOwnKeyAgain()
    {
        // Arrange: a callback that asks for one more round, as a settle that found new work does.
        var rounds = 0;
        var self = new StrongBox<Debouncer<string>>();
        using var debouncer = self.Value = new Debouncer<string>(Window, key =>
        {
            if (++rounds < 2)
            {
                self.Value!.Schedule(key);
            }
        }, _clock);

        // Act.
        debouncer.Schedule("a");
        _clock.Advance(Window);
        _clock.Advance(Window);
        _clock.Advance(Window);

        // Assert.
        Assert.Equal(2, rounds);
    }

    [Fact]
    public void AWindowOfNothing_IsRefused()
    {
        // Act and assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Debouncer<string>(TimeSpan.Zero, _ => { }, _clock));
    }
}
