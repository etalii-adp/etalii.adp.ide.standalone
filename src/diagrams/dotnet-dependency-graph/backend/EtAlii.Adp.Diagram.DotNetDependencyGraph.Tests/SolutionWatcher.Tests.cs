using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The watcher's own wiring: does a change to a file the graph was derived from wake it, and
/// does a change to something else leave it alone.
/// </summary>
/// <remarks>
/// <para>
/// These are the only tests in this module that wait on the file system, and they wait for a
/// signal rather than sleeping a fixed time - a fixed sleep is either flaky or slow, and on a
/// loaded machine it is both.
/// </para>
/// <para>
/// <b>What the burst test may and may not assert.</b> It used to write ten files and assert
/// EXACTLY ONE report. Nothing bounds those ten writes to land inside the settle delay, so on a
/// contended machine the watcher settles twice - behaving exactly as specified - and the test
/// reports a failure. It did, on two gates, on branches that touched no C# at all. An exact count
/// over a window the test cannot bound asserts a property of the MACHINE rather than of the code.
/// The two properties the watcher actually promises are bounded and are what is asserted now:
/// <b>collapsing</b>, that ten events produce far fewer than ten reports, which is a ceiling and
/// never 1; and <b>termination</b>, that once writing stops a report arrives and nothing follows
/// it, a window the test controls because it decides when to stop writing.
/// </para>
/// <para>
/// <b>Why the negative tests end by proving the watcher was alive.</b> "Nothing arrived in 500 ms"
/// passes when the watcher is slow, and passes just as well when it is DEAD - the stronger and
/// likelier regression. Neither negative test carried any evidence that its watcher could have
/// spoken during the window it measured, and the positive test is a different instance in a
/// different method, so it is no floor for them. Each now ends with a stimulus that MUST produce a
/// report, so an absence means "nothing arrived, and something would have".
/// </para>
/// <para>
/// <b>Both of those changes LOOSEN what is asserted</b>, which is the hazard in this file: an
/// over-strict assertion relaxed carelessly becomes a vacuous one, and a test reporting health it
/// cannot vouch for is worse than the flake it replaced. The collapsing ceiling was therefore
/// checked against a planted regression - deleting the <c>_settleTimer?.Dispose()</c> in
/// <c>OnFileSystemEvent</c>, so every event schedules its own timer - and seen to fail.
/// </para>
/// </remarks>
public sealed class SolutionWatcherTests : IDisposable
{
    /// <summary>
    /// Far above any plausible mid-burst stall, so the burst test's precondition holds by
    /// construction rather than by luck. The old 150 ms was inside the range a loaded machine
    /// routinely stalls for, which is what made an exact count a lottery.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    /// <summary>Short, for the tests whose subject is a filter rather than a debounce.</summary>
    private static readonly TimeSpan Brisk = TimeSpan.FromMilliseconds(20);

    /// <summary>Generous, because it bounds a POSITIVE claim: too short only ever adds flakes.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Ten events must collapse to far fewer than ten reports. It is a ceiling rather than an
    /// exact count on purpose: 1 is the overwhelmingly likely outcome and asserting it is what
    /// made this test a lottery, while the regression it guards against - a debounce that no
    /// longer restarts - produces one report per event and is nowhere near this.
    /// </summary>
    private const int CollapsedCeiling = 4;

    private readonly string _root = Directory.CreateTempSubdirectory("ddg-watch-").FullName;

    private string Write(string name, string content)
    {
        var full = Path.Combine(_root, name);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void AChangeToAWatchedFile_ReportsTheGraphStale()
    {
        // Arrange.
        var watched = Write("Solution.slnx", "<Solution />");
        using var watcher = new SolutionWatcher([watched], Brisk);
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();

        // Act.
        File.WriteAllText(watched, "<Solution><Project Path=\"A.csproj\" /></Solution>");

        // Assert.
        Assert.True(stale.Wait(Patience, TestContext.Current.CancellationToken), "The watcher did not report the change.");
    }

    [Fact]
    public void AChangeToAFileTheGraphNeverRead_IsIgnored()
    {
        // The pairing, and the one that matters: a watcher waking on everything in a directory
        // would rebuild the graph for build output, editor swap files and its own .adp writes.
        // Without this, the test above passes for a watcher that fires on literally anything.

        // Arrange.
        var watched = Write("Solution.slnx", "<Solution />");
        var unrelated = Path.Combine(_root, "build.log");
        using var watcher = new SolutionWatcher([watched], Brisk);
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();

        // Act. Written into the SAME directory the watcher is watching, which is the point:
        // the filter is per file, not per directory.
        File.WriteAllText(unrelated, "not a file this graph was derived from");

        // Assert. A short wait: this asserts an absence, so it can only ever be evidence rather
        // than proof - but a wait many times the settle delay makes it good evidence.
        Assert.False(stale.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken), "The watcher woke for a file the graph never read.");

        // THE LIVENESS CONTROL, without which the silence above is worthless. A watcher that
        // died, or was never wired to the directory at all, produces exactly the same silence as
        // one that discriminated correctly - and the silence is the whole assertion. So the test
        // ends by giving it something it MUST report.
        File.WriteAllText(watched, "<Solution><Project Path=\"A.csproj\" /></Solution>");
        Assert.True(
            stale.Wait(Patience, TestContext.Current.CancellationToken),
            "The watcher reported nothing for the unrelated file - and nothing for a watched one either, so it was dead rather than discriminating, and the absence above said nothing.");
    }

    [Fact]
    public void ABurstOfChanges_CollapsesAndThenStops()
    {
        // A build or a restore rewrites many files at once; a graph rebuilt per file-system
        // event would rebuild dozens of times for one logical change.

        // Arrange.
        var first = Write("Solution.slnx", "<Solution />");
        var second = Write("A.csproj", "<Project />");
        using var watcher = new SolutionWatcher([first, second], Settle);
        var reports = 0;
        using var reported = new ManualResetEventSlim();
        watcher.Stale += (_, _) =>
        {
            // ReSharper disable once AccessToModifiedClosure
            // Reason: We are running a unit test here.
            Interlocked.Increment(ref reports);
            // ReSharper disable once AccessToDisposedClosure
            reported.Set();
        };

        // Act. Ten events, as a restore would produce.
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(first, $"<Solution /><!-- {i} -->");
            File.WriteAllText(second, $"<Project /><!-- {i} -->");
        }

        // Assert, first: something arrived at all.
        Assert.True(reported.Wait(Patience, TestContext.Current.CancellationToken), "The watcher did not report the burst.");

        // Let the debounce drain. Writing stopped before this, so this window belongs to the
        // watcher alone and the test can bound it.
        Thread.Sleep(Settle * 3);
        var settled = Volatile.Read(ref reports);

        // COLLAPSING. Ten events, far fewer reports. A debounce that stopped restarting reports
        // once per event and lands an order of magnitude above this ceiling.
        Assert.True(
            settled <= CollapsedCeiling,
            $"Ten file-system events produced {settled} reports, above the ceiling of {CollapsedCeiling}: the burst is no longer collapsing.");

        // TERMINATION. Writing has stopped and the watcher has settled, so nothing more may
        // arrive. This is the half the test CAN bound, because it decides when writing ends.
        reported.Reset();
        Assert.False(
            reported.Wait(Settle * 3, TestContext.Current.CancellationToken),
            "A report arrived after writing had stopped and the watcher had already settled.");
        Assert.Equal(settled, Volatile.Read(ref reports));
    }

    [Fact]
    public void AfterDisposal_NothingIsReported()
    {
        // A watcher outliving its session would push refreshes into a disposed connection.

        // Arrange.
        var watched = Write("Solution.slnx", "<Solution />");
        var watcher = new SolutionWatcher([watched], Brisk);
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();

        // THE CONTROL, established BEFORE the stimulus. A disposed watcher's silence is only
        // evidence if the write it stayed silent for produced a file-system event at all - and
        // this test cannot ask the disposed watcher that. A second, live watcher on the same file
        // answers it: if IT hears nothing either, the stimulus never happened and the silence
        // below says nothing about disposal.
        using var live = new SolutionWatcher([watched], Brisk);
        using var liveHeard = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        live.Stale += (_, _) => liveHeard.Set();

        watcher.Dispose();

        // Act. One write, heard by the control and not by the disposed watcher.
        File.WriteAllText(watched, "<Solution><Project Path=\"A.csproj\" /></Solution>");

        // Assert.
        Assert.True(
            liveHeard.Wait(Patience, TestContext.Current.CancellationToken),
            "The control watcher heard nothing either, so the write produced no event and the disposed watcher's silence is not evidence.");
        Assert.False(stale.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken), "A disposed watcher still reported.");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder a virus scanner still holds is not a test failure.
        }
    }
}
