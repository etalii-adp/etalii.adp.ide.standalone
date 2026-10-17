using System.Collections.Concurrent;
using EtAlii.Adp.Files;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A store's folder inside a project root that is watched already hears its changes from that
/// root's watcher, and only its own; a folder outside every watched root keeps a watcher of its own
/// (code-structure T148, FR-050).
/// </summary>
public sealed class WatchedFolderStoreWithinAProjectTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-store-within-").FullName;
    private readonly ProjectFileEvents _events = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly CountingStore _store;

    public WatchedFolderStoreWithinAProjectTests()
    {
        _store = new CountingStore(_events, _clock);
    }

    public void Dispose()
    {
        _store.Dispose();
        _events.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task AFolderInsideAWatchedProject_ListensThroughTheProjectsWatcher_AndHearsItsChanges()
    {
        // Arrange.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "chart")).FullName;
        using var project = _events.Subscribe(_root, _ => { });
        _store.Acquire(folder);
        var changes = new ConcurrentQueue<string>();
        using var watch = _store.Watch(folder, changes.Enqueue);

        // Act.
        await File.WriteAllTextAsync(IoPath.Combine(folder, "values.yaml"), "a: 1", TestContext.Current.CancellationToken);

        // Assert: the project's one watcher, and the change heard through it.
        Assert.Same(_events.WatcherOf(_root), Assert.Single(_store.WatchersOf(folder)));
        Assert.True(await Eventually.TrueAsync(() => Settled(() => !changes.IsEmpty), TestContext.Current.CancellationToken, Patience), "The change in the folder was not heard.");
    }

    [Fact]
    public async Task AChangeElsewhereInTheProject_IsNotTheFoldersChange()
    {
        // Arrange.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "chart")).FullName;
        var sibling = Directory.CreateDirectory(IoPath.Combine(_root, "chart-other")).FullName;
        using var project = _events.Subscribe(_root, _ => { });
        _store.Acquire(folder);
        var changes = new ConcurrentQueue<string>();
        using var watch = _store.Watch(folder, changes.Enqueue);

        // Act: a sibling whose name starts with the folder's, and the root itself.
        await File.WriteAllTextAsync(IoPath.Combine(sibling, "values.yaml"), "a: 1", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "readme.md"), "text", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(await Eventually.TrueAsync(() => Settled(() => !changes.IsEmpty), TestContext.Current.CancellationToken, Silence), "A change outside the folder was the folder's.");
    }

    [Fact]
    public void AFolderInsideNoWatchedProject_KeepsAWatcherOfItsOwn()
    {
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "chart")).FullName;

        _store.Acquire(folder);

        Assert.Single(_store.WatchersOf(folder));
        Assert.Equal(0, _events.Roots);
    }

    /// <summary><paramref name="probe"/>, asked once the store's clock has moved a settle delay.</summary>
    private bool Settled(Func<bool> probe)
    {
        _clock.Advance(SettleDelay);
        return probe();
    }

    /// <summary>A store whose every read is a new snapshot, so each settled change is told.</summary>
    private sealed class CountingStore(ProjectFileEvents events, TimeProvider time) : WatchedFolderStore<string>(SettleDelay, events, time)
    {
        protected override string Read(string folder) => Guid.NewGuid().ToString("N");
    }
}
