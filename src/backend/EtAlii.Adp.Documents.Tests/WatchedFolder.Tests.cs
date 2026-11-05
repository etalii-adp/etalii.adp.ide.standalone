using System.Collections.Concurrent;
using System.Reflection;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The one place a watcher is made: every event a publish raises, an overflow as an event of its
/// own, subscribed before enabled, every handler guarded (code-structure T145, FR-050).
/// </summary>
public sealed class WatchedFolderTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly string[] Events = ["Changed", "Created", "Renamed", "Deleted", "Error"];

    private readonly TestFolder _testFolder = TestFolder.Create("adp-watched-folder");
    private readonly string _root;

    public WatchedFolderTests() => _root = _testFolder.Path;

    public void Dispose() => _testFolder.Dispose();

    [Fact]
    public void TheWatcher_IsEnabled_WithAHandlerForEveryEventAPublishRaises()
    {
        using var folder = new WatchedFolder(_root, _ => { });

        Assert.True(folder.Watcher().EnableRaisingEvents);
        foreach (var name in Events)
        {
            Assert.True(HasHandler(folder.Watcher(), name), $"The watcher has no handler for {name}.");
        }
    }

    [Fact]
    public async Task TheWatcher_IsSubscribedBeforeItIsEnabled()
    {
        // A handler attached after the watcher was enabled is attached by now too, so a running
        // watcher cannot say which came first; the source can. A watcher enabled first is live with
        // nobody listening, and what it raises then is lost.
        var source = await File.ReadAllTextAsync(RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Files", "WatchedFolder.cs"), TestContext.Current.CancellationToken);
        var enabled = source.IndexOf("Watcher.EnableRaisingEvents = true", StringComparison.Ordinal);
        Assert.True(enabled > 0, "WatchedFolder no longer enables its watcher the way this test reads it.");
        foreach (var name in Events)
        {
            var subscribed = source.IndexOf($"Watcher.{name} +=", StringComparison.Ordinal);
            Assert.True(subscribed > 0, $"WatchedFolder no longer subscribes to {name} the way this test reads it.");
            Assert.True(subscribed < enabled, $"WatchedFolder subscribes to {name} after enabling its watcher.");
        }
    }

    [Fact]
    public void ARename_NamesBothTheOldAndTheNewName()
    {
        // Arrange.
        var before = IoPath.Combine(_root, "before.txt");
        var after = IoPath.Combine(_root, "after.txt");
        var heard = new ConcurrentQueue<FileEvent>();
        using var folder = new WatchedFolder(_root, heard.Enqueue);

        // Act: raised on the calling thread, so it has been heard by now.
        folder.Watcher().RaiseRenamed(before, after);

        // Assert.
        Assert.Equal([new FileEvent(FileEventKind.Renamed, after, before)], heard);
    }

    [Fact]
    public void AMoveSplitByTheWatcher_IsPassedOnAsItWasRaised()
    {
        // Arrange: on Linux a move whose halves arrive more than 2 ms apart is raised as a Deleted
        // and a Created. Putting them back together is ProjectFileEvents' work (SplitMoveJoiner),
        // not the folder's, which passes on what the watcher raised.
        var before = IoPath.Combine(_root, "before.txt");
        var after = IoPath.Combine(_root, "after.txt");
        var heard = new ConcurrentQueue<FileEvent>();
        using var folder = new WatchedFolder(_root, heard.Enqueue);

        // Act.
        folder.Watcher().RaiseSplitMove(before, after);

        // Assert.
        Assert.Equal([new FileEvent(FileEventKind.Deleted, before), new FileEvent(FileEventKind.Created, after)], heard);
    }

    /// <summary>
    /// A real move, heard under both its names: as one Renamed when the watcher paired its halves,
    /// or as a Deleted of the old name and a Created of the new one when it did not.
    /// </summary>
    /// <remarks>
    /// This test once waited for a Renamed alone, and failed once in twenty nightly runs (Stability
    /// run 13, 2026-10-17): "The rename was never heard". The watcher had split the move, which it
    /// does on Linux when the second half arrives after its 2 ms poll, as on a loaded runner. Fed the
    /// split through the watcher, that wait failed every time (code-structure T291).
    /// </remarks>
    [Fact]
    public async Task AMoveOnDisk_IsHeardUnderBothNames()
    {
        // Arrange.
        var before = IoPath.Combine(_root, "before.txt");
        var after = IoPath.Combine(_root, "after.txt");
        await File.WriteAllTextAsync(before, "text", TestContext.Current.CancellationToken);
        var heard = new ConcurrentQueue<FileEvent>();
        using var folder = new WatchedFolder(_root, heard.Enqueue);

        // Act.
        File.Move(before, after);

        // Assert.
        Assert.True(
            await Eventually.TrueAsync(() => IsHeardUnderBothNames(heard, before, after), TestContext.Current.CancellationToken, Patience),
            "The move was not heard under both names; heard: " + string.Join(", ", heard));
    }

    [Fact]
    public void AnOverflow_IsHeardAsLost_NamingTheFolder()
    {
        var heard = new ConcurrentQueue<FileEvent>();
        using var folder = new WatchedFolder(_root, heard.Enqueue);

        folder.Watcher().RaiseOverflow();

        // The watcher raises its Error on the calling thread, so it has been heard by now.
        Assert.Equal([new FileEvent(FileEventKind.Lost, _root)], heard);
    }

    [Fact]
    public async Task AHandlerThatThrows_LosesOnlyThatEvent()
    {
        // Arrange: the first event's handler throws; on the watcher's thread that would end the process unguarded.
        var heard = new ConcurrentQueue<FileEvent>();
        var thrown = 0;
        using var folder = new WatchedFolder(_root, e =>
        {
            if (Interlocked.Increment(ref thrown) == 1)
            {
                throw new InvalidOperationException("The first event cannot be handled.");
            }

            heard.Enqueue(e);
        });

        // Act.
        folder.Watcher().RaiseOverflow();
        await File.WriteAllTextAsync(IoPath.Combine(_root, "later.txt"), "text", TestContext.Current.CancellationToken);

        // Assert: what came after was still heard.
        Assert.True(await Eventually.TrueAsync(() => !heard.IsEmpty, TestContext.Current.CancellationToken, Patience), "Nothing was heard after a handler threw.");
    }

    /// <summary>A Renamed from <paramref name="before"/> to <paramref name="after"/>, or a Deleted of the one and a Created of the other.</summary>
    private static bool IsHeardUnderBothNames(IEnumerable<FileEvent> heard, string before, string after)
    {
        var events = heard.ToList();
        return events.Contains(new FileEvent(FileEventKind.Renamed, after, before))
            || (events.Contains(new FileEvent(FileEventKind.Deleted, before)) && events.Contains(new FileEvent(FileEventKind.Created, after)));
    }

    private static bool HasHandler(FileSystemWatcher watcher, string eventName)
    {
        // FileSystemWatcher keeps each event's handlers in a private field named for the event.
        var field = typeof(FileSystemWatcher).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name.Contains(eventName, StringComparison.OrdinalIgnoreCase) && typeof(Delegate).IsAssignableFrom(candidate.FieldType))
            ?? throw new InvalidOperationException($"FileSystemWatcher no longer keeps its {eventName} handlers where this test looks.");
        return field.GetValue(watcher) is Delegate;
    }
}
