using System.Collections.Concurrent;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A folder tree inside a watched project root is heard through that root's one watcher, and only
/// its own events; outside every watched root it gets a watcher of its own (code-structure T280, FR-050).
/// </summary>
public sealed class WatchedTreeTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(300);

    private readonly TestFolder _testFolder = TestFolder.Create("adp-watched-tree");
    private readonly string _root;
    private readonly ProjectFileEvents _events = new();

    public WatchedTreeTests() => _root = _testFolder.Path;

    public void Dispose()
    {
        _events.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task ATreeInsideAWatchedRoot_IsHeardThroughTheRootsWatcher_AndMakesNoneOfItsOwn()
    {
        // Arrange: the folders are there before the project is watched, as in a project that is opened.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "charts", "web")).FullName;
        var path = IoPath.Combine(folder, "templates", "deployment.yaml");
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        using var project = _events.Subscribe(_root, _ => { });
        var heard = new ConcurrentQueue<FileEvent>();
        using var tree = new WatchedTree(folder, heard.Enqueue, _events);

        // Act.
        await File.WriteAllTextAsync(path, "kind: Deployment", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(tree.HasWatcherOfItsOwn());
        Assert.Equal(1, _events.WatchedRoots());
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "A write below the tree was not heard through the root.");
    }

    [Fact]
    public async Task AWriteBesideTheTree_IsNotHeard()
    {
        // Arrange: a sibling whose name begins with the tree's, the case a prefix test gets wrong.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "web")).FullName;
        var beside = Directory.CreateDirectory(IoPath.Combine(_root, "web-other")).FullName;
        using var project = _events.Subscribe(_root, _ => { });
        var heard = new ConcurrentQueue<FileEvent>();
        using var tree = new WatchedTree(folder, heard.Enqueue, _events);
        var heardByTheRoot = new ConcurrentQueue<FileEvent>();
        using var root = _events.Subscribe(_root, heardByTheRoot.Enqueue);
        var path = IoPath.Combine(beside, "values.yaml");

        // Act.
        await File.WriteAllTextAsync(path, "replicaCount: 1", TestContext.Current.CancellationToken);

        // Assert: the root heard it, so the tree was told and passed it by.
        Assert.True(await Eventually.TrueAsync(() => heardByTheRoot.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "The root never heard the write.");
        Assert.False(await Eventually.TrueAsync(() => !heard.IsEmpty, TestContext.Current.CancellationToken, Silence), "A write beside the tree was heard: " + string.Join(", ", heard));
    }

    [Fact]
    public async Task AFileMovedOutOfTheTree_IsHeardByItsOldName()
    {
        // Arrange.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "web")).FullName;
        var before = IoPath.Combine(folder, "values.yaml");
        var after = IoPath.Combine(_root, "values.yaml");
        await File.WriteAllTextAsync(before, "replicaCount: 1", TestContext.Current.CancellationToken);
        using var project = _events.Subscribe(_root, _ => { });
        var heard = new ConcurrentQueue<FileEvent>();
        using var tree = new WatchedTree(folder, heard.Enqueue, _events);

        // Act.
        File.Move(before, after);

        // Assert: a file moved away changes the tree as much as one moved in.
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == before || e.OldPath == before), TestContext.Current.CancellationToken, Patience), "The move out of the tree was not heard.");
    }

    [Fact]
    public void ALostEventOfTheRoot_IsPassedOn()
    {
        // Arrange.
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "web")).FullName;
        using var project = _events.Subscribe(_root, _ => { });
        var heard = new ConcurrentQueue<FileEvent>();
        using var tree = new WatchedTree(folder, heard.Enqueue, _events);

        // Act: raised on the calling thread, so it has been heard by now.
        _events.Watcher(_root)!.RaiseOverflow();

        // Assert.
        Assert.Contains(heard, e => e.Kind == FileEventKind.Lost);
    }

    [Fact]
    public async Task ATreeOutsideEveryWatchedRoot_IsWatchedOnItsOwn()
    {
        // Arrange: a root is watched, and the tree is not under it.
        var project = Directory.CreateDirectory(IoPath.Combine(_root, "project")).FullName;
        var folder = Directory.CreateDirectory(IoPath.Combine(_root, "elsewhere")).FullName;
        var path = IoPath.Combine(folder, "nested", "values.yaml");
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        using var watched = _events.Subscribe(project, _ => { });
        var heard = new ConcurrentQueue<FileEvent>();
        using var tree = new WatchedTree(folder, heard.Enqueue, _events);

        // Act.
        await File.WriteAllTextAsync(path, "replicaCount: 1", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(tree.HasWatcherOfItsOwn());
        Assert.True(await Eventually.TrueAsync(() => heard.Any(e => e.Path == path), TestContext.Current.CancellationToken, Patience), "A write below the tree was not heard by its own watcher.");
    }
}
