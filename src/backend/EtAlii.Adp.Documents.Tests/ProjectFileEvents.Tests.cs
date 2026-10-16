using System.Collections.Concurrent;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// One source of file events per project root: one watcher however many listen, events about a
/// writer's own files normalized away, every subscriber guarded, and a stumble heard as Lost
/// (code-structure T147, FR-050).
/// </summary>
public sealed class ProjectFileEventsTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(300);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-project-file-events-").FullName;
    private readonly ProjectFileEvents _events = new();

    public void Dispose()
    {
        _events.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void SubscriptionsToOneRoot_ShareOneWatcher_UntilTheLastIsDisposed()
    {
        // Arrange: the second spells the root with a trailing separator, as a caller may.
        var first = _events.Subscribe(_root, _ => { });
        var second = _events.Subscribe(_root + IoPath.DirectorySeparatorChar, _ => { });
        var watcher = _events.WatcherOf(_root);

        // Act & Assert: one watcher for both, kept while either listens.
        Assert.Equal(1, _events.Roots);
        first.Dispose();
        Assert.Equal(1, _events.Roots);
        Assert.Same(watcher, _events.WatcherOf(_root));
        second.Dispose();
        Assert.Equal(0, _events.Roots);
        Assert.Null(_events.WatcherOf(_root));
    }

    [Fact]
    public async Task AWrite_ReachesEverySubscriber()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "body.txt");
        var one = new ConcurrentQueue<FileEvent>();
        var other = new ConcurrentQueue<FileEvent>();
        using var first = _events.Subscribe(_root, one.Enqueue);
        using var second = _events.Subscribe(_root, other.Enqueue);

        // Act.
        await File.WriteAllTextAsync(path, "text", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => one.Any(e => e.Path == path) && other.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "The write did not reach both subscribers.");
    }

    [Fact]
    public async Task AWriteInASubfolder_IsHeard()
    {
        // Arrange.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "nested")).FullName;
        var path = IoPath.Combine(folder, "body.txt");
        var heard = new ConcurrentQueue<FileEvent>();
        using var subscription = _events.Subscribe(_root, heard.Enqueue);

        // Act.
        await File.WriteAllTextAsync(path, "text", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "A write below the root was not heard.");
    }

    [Fact]
    public async Task ASaveThroughTheWriter_IsHeardAsItsDestination_NeverAsAWritersOwnFile()
    {
        // Arrange: a destination that exists, so the save replaces it, and one that does not.
        var replaced = IoPath.Combine(_root, "replaced.txt");
        var created = IoPath.Combine(_root, "created.txt");
        await File.WriteAllTextAsync(replaced, "before", TestContext.Current.CancellationToken);
        var heard = new ConcurrentQueue<FileEvent>();
        using var subscription = _events.Subscribe(_root, heard.Enqueue);

        // Act.
        AdpFileWriter.Save(replaced, "after");
        AdpFileWriter.Save(created, "new");

        // Assert: both destinations heard; no transient name, no destination gone.
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == replaced) && heard.Any(e => e.Path == created), TestContext.Current.CancellationToken, Patience), "A save was not heard as its destination.");
        Assert.False(
            await Eventually.TrueAsync(() => heard.Any(e => AdpFileWriter.IsTransient(e.Path) || (e.OldPath is not null && AdpFileWriter.IsTransient(e.OldPath)) || e.Kind == FileEventKind.Deleted), TestContext.Current.CancellationToken, Silence),
            "A writer's own file, or a destination gone, was heard.");
    }

    [Fact]
    public async Task ASubscriberThatThrows_CostsNoOtherSubscriberItsEvent()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "body.txt");
        var heard = new ConcurrentQueue<FileEvent>();
        using var failing = _events.Subscribe(_root, _ => throw new InvalidOperationException("A subscriber that fails."));
        using var listening = _events.Subscribe(_root, heard.Enqueue);

        // Act.
        await File.WriteAllTextAsync(path, "text", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "A failing subscriber cost the next one its event.");
    }

    [Fact]
    public async Task AnOverflow_IsHeardAsLost_AndTheRootIsWatchedAgain()
    {
        // Arrange.
        var heard = new ConcurrentQueue<FileEvent>();
        using var subscription = _events.Subscribe(_root, heard.Enqueue);
        var stumbled = _events.WatcherOf(_root)!;

        // Act.
        stumbled.RaiseOverflow();
        var path = IoPath.Combine(_root, "after-the-overflow.txt");
        await File.WriteAllTextAsync(path, "text", TestContext.Current.CancellationToken);

        // Assert: Lost for the root, a new watcher in place of the one that stumbled, and that one hearing.
        Assert.Contains(heard, e => e.Kind == FileEventKind.Lost && e.Path == _root);
        Assert.NotSame(stumbled, _events.WatcherOf(_root));
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "Nothing was heard after the overflow.");
    }

    [Fact]
    public void ARootThatDoesNotExist_IsRefused_AndLeavesNothingWatched()
    {
        var missing = IoPath.Combine(_root, "missing");

        Assert.ThrowsAny<ArgumentException>(() => _events.Subscribe(missing, _ => { }));
        Assert.Equal(0, _events.Roots);
    }

    [Fact]
    public void AFolderInsideAWatchedRoot_IsHeardThroughThatRootsWatcher()
    {
        // Arrange.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "charts", "web")).FullName;
        var project = _events.Subscribe(_root, _ => { });
        var watcher = _events.WatcherOf(_root);

        // Act.
        var within = _events.SubscribeWithin(folder, _ => { });
        project.Dispose();

        // Assert: the root's own watcher, kept by the folder now the project's subscriber left.
        Assert.NotNull(within);
        Assert.Equal(_root, within.Value.Root);
        Assert.Equal(1, _events.Roots);
        Assert.Same(watcher, _events.WatcherOf(_root));
        within.Value.Subscription.Dispose();
        Assert.Equal(0, _events.Roots);
    }

    [Fact]
    public void AFolderInsideNoWatchedRoot_IsNotSubscribed()
    {
        using var other = _events.Subscribe(_root, _ => { });
        var outside = Directory.CreateTempSubdirectory("adp-project-file-events-outside-").FullName;
        try
        {
            Assert.Null(_events.SubscribeWithin(outside, _ => { }));
            Assert.Null(_events.SubscribeWithin(_root + "-sibling", _ => { }));
        }
        finally
        {
            TestFolder.TryDelete(outside);
        }
    }

    [Fact]
    public async Task WatchingARootAgain_MakesANewWatcher_ThatHears()
    {
        // Arrange.
        var heard = new ConcurrentQueue<FileEvent>();
        using var subscription = _events.Subscribe(_root, heard.Enqueue);
        var before = _events.WatcherOf(_root);

        // Act.
        _events.WatchAgain(_root);
        var path = IoPath.Combine(_root, "after.txt");
        await File.WriteAllTextAsync(path, "text", TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotSame(before, _events.WatcherOf(_root));
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "Nothing was heard through the new watcher.");
    }

    [Theory]
    [InlineData("/p/a", "/p/a", true)]
    [InlineData("/p/a/b.txt", "/p/a", true)]
    [InlineData("/p/a/b/c.txt", "/p/a", true)]
    [InlineData("/p/ab", "/p/a", false)]
    [InlineData("/p", "/p/a", false)]
    [InlineData("/P/A/b.txt", "/p/a", true)]
    public void IsWithin_IsTheFolderOrBelowIt_NeverASiblingThatSharesItsName(string path, string folder, bool within) =>
        Assert.Equal(within, ProjectFileEvents.IsWithin(path, folder));

    private const string Scratch = "/p/~adp-0123456789abcdef0123456789abcdef.tmp";
    private const string Backup = "/p/body.dsl~RF1a2b3c.TMP";

    public static TheoryData<FileEventKind, string, string?, FileEventKind?, string?> Normalized => new()
    {
        // Project content passes as it came.
        { FileEventKind.Changed, "/p/body.dsl", null, FileEventKind.Changed, "/p/body.dsl" },
        { FileEventKind.Renamed, "/p/after.dsl", "/p/before.dsl", FileEventKind.Renamed, "/p/after.dsl" },
        { FileEventKind.Lost, "/p", null, FileEventKind.Lost, "/p" },

        // A writer's own files are not.
        { FileEventKind.Created, Scratch, null, null, null },
        { FileEventKind.Deleted, Backup, null, null, null },

        // The body moved aside for the instant of a replace is not gone.
        { FileEventKind.Renamed, Backup, "/p/body.dsl", null, null },

        // The scratch file taking the destination's name is the publish it gets.
        { FileEventKind.Renamed, "/p/body.dsl", Scratch, FileEventKind.Created, "/p/body.dsl" },
    };

    [Theory]
    [MemberData(nameof(Normalized))]
    public void AnEvent_IsNormalized(FileEventKind kind, string path, string? oldPath, FileEventKind? expectedKind, string? expectedPath)
    {
        var normalized = ProjectFileEvents.Normalize(new FileEvent(kind, path, oldPath));

        Assert.Equal(expectedKind, normalized?.Kind);
        Assert.Equal(expectedPath, normalized?.Path);
        if (normalized is { Kind: FileEventKind.Renamed } renamed)
        {
            Assert.Equal(oldPath, renamed.OldPath);
        }
    }
}
