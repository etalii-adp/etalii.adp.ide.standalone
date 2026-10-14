using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What every watched-folder store must do, written once (code-structure FR-037): one snapshot per
/// folder, read once and handed out by reference; a change on disk re-read before it is announced;
/// a burst costing fewer re-reads than it has changes; a folder kept while anyone claims it and
/// dropped when the last claim goes; and a watch that hears only its own folder and nothing once
/// it is let go. A module's store test derives from this and supplies its store, a folder and a
/// change its snapshot shows.
/// </summary>
/// <remarks>
/// Against a fresh folder per test, because these tests deliberately change files.
/// </remarks>
public abstract class WatchedFolderStoreContract<TSnapshot> : IDisposable
    where TSnapshot : class
{
    /// <summary>The settle delay the store is made with: short, so a test waits for little.</summary>
    /// <remarks>Properties rather than fields: a static field of a generic type is one per closed type.</remarks>
    private static TimeSpan SettleDelay => TimeSpan.FromMilliseconds(150);

    private static TimeSpan WaitLimit => TimeSpan.FromSeconds(15);

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.WatchedFolderStoreContract");

    /// <summary>The store under test, made on first use and disposed with the test.</summary>
    protected WatchedFolderStore<TSnapshot> Store => field ??= CreateStore(SettleDelay);

    /// <summary>The folder the test works on, filled on first use.</summary>
    protected string Root => field ??= Fill("root");

    /// <summary>A new store, with nothing loaded, that settles after <paramref name="settleDelay"/>.</summary>
    protected abstract WatchedFolderStore<TSnapshot> CreateStore(TimeSpan settleDelay);

    /// <summary>Fills the empty folder <paramref name="root"/> with something the store reads.</summary>
    protected abstract void Populate(string root);

    /// <summary>Changes a file in <paramref name="root"/> so that <see cref="ShowsTheChange"/> holds of its next read.</summary>
    protected abstract Task ChangeAsync(string root, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="snapshot"/> was read after <see cref="ChangeAsync"/>.</summary>
    protected abstract bool ShowsTheChange(TSnapshot snapshot);

    /// <summary>Changes a file in <paramref name="root"/> for the <paramref name="index"/>th time, as a burst of saves does.</summary>
    protected abstract Task TouchAsync(string root, int index, CancellationToken cancellationToken);

    private string Fill(string name)
    {
        var root = _folder.Combine(name);
        Directory.CreateDirectory(root);
        Populate(root);
        return root;
    }

    public void Dispose()
    {
        Store.Dispose();
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetOrLoad_ReadsOnce_AndHandsTheSameSnapshotBack()
    {
        // Act.
        var first = Store.GetOrLoad(Root);
        var second = Store.GetOrLoad(Root);

        // Assert: the same instance, not merely an equal one - one snapshot per folder is what
        // stops two connections seeing different things.
        Assert.Same(first, second);
    }

    [Fact]
    public void Get_DoesNotLoad()
    {
        // Act and assert, step by step.
        Assert.Null(Store.Get(Root));
        Store.GetOrLoad(Root);
        Assert.NotNull(Store.Get(Root));
    }

    [Fact]
    public async Task AChange_IsAnnouncedWithTheRereadSnapshot_AndTheStoreHandsThatOneOut()
    {
        // Arrange.
        Store.GetOrLoad(Root);
        var changes = Changes();

        // Act.
        await ChangeAsync(Root, TestContext.Current.CancellationToken);

        // Assert: re-read before announcing, so a listener that asks the store during the event
        // is not handed the snapshot the event exists to replace.
        var snapshot = await WaitFor(changes, 1);
        Assert.True(ShowsTheChange(snapshot));
        Assert.Same(snapshot, Store.Get(Root));
    }

    [Fact]
    public async Task ABurstOfChanges_CostsFarFewerRereadsThanChanges()
    {
        // Arrange.
        const int changeCount = 6;
        Store.GetOrLoad(Root);
        var changes = Changes();

        // Act: written back to back, as a multi-file save arrives. Any stall longer than the
        // settle delay lets the timer fire mid-burst, so exactly one re-read would be a clock test.
        for (var i = 0; i < changeCount; i++)
        {
            await TouchAsync(Root, i, TestContext.Current.CancellationToken);
        }

        // Assert.
        await WaitFor(changes, 1);
        await Task.Delay(SettleDelay + SettleDelay, TestContext.Current.CancellationToken);
        lock (changes)
        {
            Assert.InRange(changes.Count, 1, changeCount - 1);
        }
    }

    [Fact]
    public void ReleasingOneOfTwoClaims_KeepsTheFolderWatched()
    {
        // Arrange: a second connection closing must not tear the watcher out from under the
        // first, whose diagram would then go quietly stale.
        Store.Acquire(Root);
        Store.Acquire(Root);

        // Act.
        Store.Release(Root);

        // Assert.
        Assert.NotNull(Store.Get(Root));
    }

    [Fact]
    public void ReleasingTheLastClaim_DropsTheFolder()
    {
        // Arrange.
        Store.Acquire(Root);

        // Act.
        Store.Release(Root);

        // Assert.
        Assert.Null(Store.Get(Root));
    }

    [Fact]
    public async Task AReleasedFolder_IsReadAfreshOnTheNextAsk()
    {
        // Arrange.
        var before = Store.Acquire(Root);
        Store.Release(Root);

        // Act: changed while nobody watches.
        await ChangeAsync(Root, TestContext.Current.CancellationToken);
        var after = Store.GetOrLoad(Root);

        // Assert.
        Assert.NotSame(before, after);
        Assert.True(ShowsTheChange(after));
    }

    [Fact]
    public void ReleasingAFolderNobodyHeld_IsNotAnError()
    {
        // Act and assert.
        Store.Release(Root);
        Store.Release(Path.Combine(Root, "never-loaded"));
    }

    [Fact]
    public async Task AWatch_HearsItsFolder_AndNothingOnceItIsLetGo()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Root);
        var heard = new List<TSnapshot>();
        var watch = Store.Watch(Root, snapshot =>
        {
            lock (heard)
            {
                heard.Add(snapshot);
            }
        });
        var changes = Changes();

        // Act: heard while watched, then let go - twice, which is harmless - and changed again.
        await ChangeAsync(Root, cancellationToken);
        await WaitFor(changes, 1);
        watch.Dispose();
        watch.Dispose();
        await TouchAsync(Root, 0, cancellationToken);
        await WaitFor(changes, 2);

        // Assert.
        lock (heard)
        {
            var only = Assert.Single(heard);
            Assert.True(ShowsTheChange(only));
        }
    }

    [Fact]
    public async Task AWatch_DoesNotHearAnotherFolder()
    {
        // Arrange.
        var other = Fill("other");
        Store.GetOrLoad(Root);
        Store.GetOrLoad(other);
        var heard = new int[1];
        using var watch = Store.Watch(Root, _ => Interlocked.Increment(ref heard[0]));
        var changes = Changes();

        // Act.
        await ChangeAsync(other, TestContext.Current.CancellationToken);
        await WaitFor(changes, 1);

        // Assert.
        Assert.Equal(0, Volatile.Read(ref heard[0]));
    }

    /// <summary>Every snapshot the store announces from now on, whichever folder it is of.</summary>
    private List<TSnapshot> Changes()
    {
        var changes = new List<TSnapshot>();
        Store.Changed += (_, args) =>
        {
            lock (changes)
            {
                changes.Add(args.Snapshot);
            }
        };
        return changes;
    }

    private static async Task<TSnapshot> WaitFor(List<TSnapshot> changes, int count)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < WaitLimit)
        {
            lock (changes)
            {
                if (changes.Count >= count)
                {
                    return changes[^1];
                }
            }
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The folder was not re-read within {WaitLimit}.");
        return null!;
    }
}
