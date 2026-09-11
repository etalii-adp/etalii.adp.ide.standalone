using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The watcher's own wiring: does a change to a file the graph was derived from wake it, and
/// does a change to something else leave it alone.
/// </summary>
/// <remarks>
/// These are the only tests in this module that wait on the file system, and they wait for a
/// signal rather than sleeping a fixed time - a fixed sleep is either flaky or slow, and on a
/// loaded machine it is both.
/// </remarks>
public sealed class SolutionWatcherTests : IDisposable
{
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
        using var watcher = new SolutionWatcher([watched], TimeSpan.FromMilliseconds(20));
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();

        // Act.
        File.WriteAllText(watched, "<Solution><Project Path=\"A.csproj\" /></Solution>");

        // Assert.
        Assert.True(stale.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "The watcher did not report the change.");
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
        using var watcher = new SolutionWatcher([watched], TimeSpan.FromMilliseconds(20));
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
    }

    [Fact]
    public void ABurstOfChanges_SettlesIntoOneReport()
    {
        // A build or a restore rewrites many files at once; a graph rebuilt per file-system
        // event would rebuild dozens of times for one logical change.

        // Arrange.
        var first = Write("Solution.slnx", "<Solution />");
        var second = Write("A.csproj", "<Project />");
        using var watcher = new SolutionWatcher([first, second], TimeSpan.FromMilliseconds(150));
        var reports = 0;
        using var stale = new ManualResetEventSlim();
        watcher.Stale += (_, _) =>
        {
            // ReSharper disable once AccessToModifiedClosure
            // Reason: We are running a unit test here.
            Interlocked.Increment(ref reports);
            // ReSharper disable once AccessToDisposedClosure
            stale.Set();
        };

        // Act.
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(first, $"<Solution /><!-- {i} -->");
            File.WriteAllText(second, $"<Project /><!-- {i} -->");
        }

        // Assert.
        Assert.True(stale.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "The watcher did not report the burst.");
        Thread.Sleep(400); // well past the settle delay, so a second report would have arrived
        Assert.Equal(1, Volatile.Read(ref reports));
    }

    [Fact]
    public void AfterDisposal_NothingIsReported()
    {
        // A watcher outliving its session would push refreshes into a disposed connection.

        // Arrange.
        var watched = Write("Solution.slnx", "<Solution />");
        var watcher = new SolutionWatcher([watched], TimeSpan.FromMilliseconds(20));
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();
        watcher.Dispose();

        // Act.
        File.WriteAllText(watched, "<Solution><Project Path=\"A.csproj\" /></Solution>");

        // Assert.
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
