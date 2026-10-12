using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The Linux watcher reports a move as a Deleted and a Created when its two inotify halves
/// arrive more than 2 ms apart; the joiner delivers such a move as the Renamed it was, and
/// everything else as it came, in order.
/// </summary>
public class SplitMoveJoinerTests : IDisposable
{
    private readonly string _root;
    private readonly List<(WatcherChangeTypes ChangeType, string? OldPath, string? NewPath)> _delivered = new();
    private readonly ManualTimeProvider _time = new();
    private readonly SplitMoveJoiner _joiner;

    public SplitMoveJoinerTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _joiner = new SplitMoveJoiner((changeType, oldPath, newPath) => _delivered.Add((changeType, oldPath, newPath)), TimeSpan.FromSeconds(1), _time);
    }

    public void Dispose()
    {
        _joiner.Dispose();
        TestFolder.TryDelete(_root);
    }

    private string CreateFile(string name)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void ADeleteThenACreate_OfAMovedFile_IsDeliveredAsOneRename()
    {
        // Arrange.
        var original = CreateFile("original.txt");
        var renamed = IoPath.Combine(_root, "renamed.txt");
        File.Move(original, renamed);

        // Act.
        _joiner.Post(WatcherChangeTypes.Deleted, original, null);
        _joiner.Post(WatcherChangeTypes.Created, null, renamed);

        // Assert.
        Assert.Equal([(WatcherChangeTypes.Renamed, original, renamed)], _delivered);
    }

    [Fact]
    public void ADelete_WithNothingAfterIt_IsDeliveredWhenTheGraceRunsOut()
    {
        // Arrange.
        var doomed = CreateFile("doomed.txt");
        File.Delete(doomed);

        // Act.
        _joiner.Post(WatcherChangeTypes.Deleted, doomed, null);
        var heldBack = _delivered.ToList();
        _time.FireTimers();

        // Assert.
        Assert.Empty(heldBack);
        Assert.Equal([(WatcherChangeTypes.Deleted, doomed, null)], _delivered);
    }

    [Fact]
    public void ADelete_FollowedByAnotherKindOfEvent_IsDeliveredFirst()
    {
        // Arrange.
        var doomed = CreateFile("doomed.txt");
        var other = CreateFile("other.txt");
        File.Delete(doomed);

        // Act.
        _joiner.Post(WatcherChangeTypes.Deleted, doomed, null);
        _joiner.Post(WatcherChangeTypes.Changed, null, other);
        _time.FireTimers();

        // Assert.
        Assert.Equal([(WatcherChangeTypes.Deleted, doomed, null), (WatcherChangeTypes.Changed, null, other)], _delivered);
    }

    [Fact]
    public void ADeleteThenACreate_OfTheSamePath_IsNotARename()
    {
        // Arrange.
        // An editor that saves by deleting the file and writing it again: the old path is back.
        var saved = CreateFile("saved.txt");

        // Act.
        _joiner.Post(WatcherChangeTypes.Deleted, saved, null);
        _joiner.Post(WatcherChangeTypes.Created, null, saved);

        // Assert.
        Assert.Equal([(WatcherChangeTypes.Created, null, saved)], _delivered.Where(d => d.ChangeType != WatcherChangeTypes.Deleted).ToList());
        Assert.DoesNotContain(_delivered, d => d.ChangeType == WatcherChangeTypes.Renamed);
    }

    [Fact]
    public void ADeleteOfAPathThatStillExists_IsDeliveredAtOnce()
    {
        // Arrange.
        // Not half of a move - the model reads it as an in-place replace.
        var replaced = CreateFile("replaced.txt");

        // Act.
        _joiner.Post(WatcherChangeTypes.Deleted, replaced, null);

        // Assert.
        Assert.Equal([(WatcherChangeTypes.Deleted, replaced, null)], _delivered);
    }

    [Fact]
    public void AHeldDelete_IsDroppedOnDispose()
    {
        // Arrange.
        var doomed = CreateFile("doomed.txt");
        File.Delete(doomed);
        _joiner.Post(WatcherChangeTypes.Deleted, doomed, null);

        // Act.
        _joiner.Dispose();
        _time.FireTimers();

        // Assert.
        Assert.Empty(_delivered);
    }

    [Fact]
    public async Task ASplitMove_KeepsTheTrackedSelectionOnTheEntry_AndItsId()
    {
        // Arrange.
        // The CI failure of ContextSelectionFlowTests.RenameOnDisk at unit scale: a file
        // selected, then renamed on disk, with the watcher reporting the move split in two. Fed
        // to the model as it came, the Deleted removed the entry and the selection tracking it
        // reported it gone - an empty selection pushed where the renamed one belonged.
        using var store = new HierarchyModelStore(idleTimeout: TimeSpan.FromMinutes(5));
        var resolver = new HierarchyContextSourceResolver(store, new DiagramFileRouter(new EmptyCatalog()), new EditorResolver(new Editor.EditorDefinitionCatalog { All = [] }));
        var watchId = ShortGuid.NewShortGuid();
        var model = store.GetOrCreate(watchId, _root);
        var original = CreateFile("original.txt");
        var entryId = model.ListChildren(null).Single(n => n.Name == "original.txt").Id;
        var resolution = await resolver.ResolveAsync(
            watchId, _root, new ContextSource { EntryId = entryId }, [], parent: null, TestContext.Current.CancellationToken);
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        var reported = new List<IReadOnlyList<string>?>();
        using var track = resolver.Track(watchId, _root, level, reported.Add);
        using var joiner = new SplitMoveJoiner(model.OnWatcherEvent, TimeSpan.FromSeconds(1), _time);
        var renamed = IoPath.Combine(_root, "renamed.txt");
        File.Move(original, renamed);

        // Act.
        joiner.Post(WatcherChangeTypes.Deleted, original, null);
        joiner.Post(WatcherChangeTypes.Created, null, renamed);
        _time.FireTimers();

        // Assert.
        Assert.Equal(["renamed.txt"], Assert.Single(reported));
        Assert.True(model.TryResolvePath(entryId, out var fullPath, out _));
        Assert.Equal(renamed, fullPath);
    }

    /// <summary>A clock whose timers fire only when the test says so.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State, ManualTimer Timer)> _timers = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer();
            _timers.Add((callback, state, timer));
            return timer;
        }

        public void FireTimers()
        {
            foreach ((TimerCallback callback, object? state, ManualTimer timer) in _timers.ToList())
            {
                if (!timer.Disposed)
                {
                    callback(state);
                }
            }
        }

        private sealed class ManualTimer : ITimer
        {
            public bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
