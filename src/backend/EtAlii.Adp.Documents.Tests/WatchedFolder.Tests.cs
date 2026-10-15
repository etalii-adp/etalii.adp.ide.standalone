using System.Collections.Concurrent;
using System.Reflection;
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

    private readonly string _root = Directory.CreateTempSubdirectory("adp-watched-folder-").FullName;

    public void Dispose() => TestFolder.TryDelete(_root);

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
        var source = await File.ReadAllTextAsync(RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Documents", "WatchedFolder.cs"), TestContext.Current.CancellationToken);
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
    public async Task ARename_NamesBothTheOldAndTheNewName()
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
            await Eventually.TrueAsync(() => heard.Any(e => e.Kind == FileEventKind.Renamed), TestContext.Current.CancellationToken, Patience),
            "The rename was never heard.");
        var renamed = heard.First(e => e.Kind == FileEventKind.Renamed);
        Assert.Equal(after, renamed.Path);
        Assert.Equal(before, renamed.OldPath);
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

    private static bool HasHandler(FileSystemWatcher watcher, string eventName)
    {
        // FileSystemWatcher keeps each event's handlers in a private field named for the event.
        var field = typeof(FileSystemWatcher).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name.Contains(eventName, StringComparison.OrdinalIgnoreCase) && typeof(Delegate).IsAssignableFrom(candidate.FieldType))
            ?? throw new InvalidOperationException($"FileSystemWatcher no longer keeps its {eventName} handlers where this test looks.");
        return field.GetValue(watcher) is Delegate;
    }
}
