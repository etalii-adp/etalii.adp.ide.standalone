using System.Reflection;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The watcher learns of every change that can make its graph stale - here a deletion and a window
/// in which events were lost - rather than merely subscribing to the events that carry them
/// (backend-centralization R2.9).
/// </summary>
/// <remarks>
/// <b>Why the overflow case is here at all.</b> This watcher subscribed to Error and only logged it,
/// so an overflow left the graph looking fresh - its own comment said an unsubscribed overflow "looks
/// exactly like a quiet solution", and a logged-only one still did. Which watched file changed is
/// unknowable, so the graph must go stale as though one had. The lost change is caused by switching
/// every watcher off while the file changes, then raising a watcher's own Error through
/// <c>FileSystemWatcher.OnError</c>.
/// </remarks>
public class SolutionWatcherObligationsTests : IDisposable
{
    private static readonly TimeSpan Brisk = TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("ddg-obligations-").FullName;

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ADeletedWatchedFile_ReportsTheGraphStale()
    {
        var watched = Write("Solution.slnx", "<Solution />");
        using var watcher = new SolutionWatcher([watched], Brisk);
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();

        File.Delete(watched);

        Assert.True(stale.Wait(Patience, TestContext.Current.CancellationToken), "The watcher never learned a file the graph was derived from was deleted.");
    }

    [Fact]
    public void ALostEventsWindow_ReportsTheGraphStale()
    {
        var watched = Write("Solution.slnx", "<Solution />");
        using var watcher = new SolutionWatcher([watched], Brisk);
        using var stale = new ManualResetEventSlim();
        // ReSharper disable once AccessToDisposedClosure
        // Reason: We are running a unit test here.
        watcher.Stale += (_, _) => stale.Set();

        // Lose the change: nothing is watching while it happens, so no event can carry it.
        var watchers = WatchersOf(watcher);
        Assert.NotEmpty(watchers);
        foreach (var each in watchers)
        {
            each.EnableRaisingEvents = false;
        }

        File.WriteAllText(watched, "<Solution><Project Path=\"A.csproj\" /></Solution>");

        RaiseOverflow(watchers[0]);

        Assert.True(stale.Wait(Patience, TestContext.Current.CancellationToken), "An overflow left the graph looking fresh.");
    }

    private static List<FileSystemWatcher> WatchersOf(SolutionWatcher watcher) =>
        typeof(SolutionWatcher).GetField("_watchers", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(watcher) as List<FileSystemWatcher>
        ?? throw new InvalidOperationException("SolutionWatcher no longer keeps its watchers in _watchers; this test must follow it.");

    // The one signal a watcher gives when it has dropped events, raised the way the watcher raises it.
    private static void RaiseOverflow(FileSystemWatcher watcher) =>
        (typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FileSystemWatcher.OnError was not found."))
        .Invoke(watcher, [new ErrorEventArgs(new InternalBufferOverflowException())]);

    private string Write(string name, string content)
    {
        var full = Path.Combine(_root, name);
        File.WriteAllText(full, content);
        return full;
    }
}
