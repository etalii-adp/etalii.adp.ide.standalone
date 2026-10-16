using EtAlii.Adp.Documents;
using Microsoft.Extensions.Time.Testing;
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
/// Against a fresh folder per test, because these tests deliberately change files. The settle delay
/// runs on <see cref="Clock"/>, which a test moves while it waits for the file system, so no test
/// waits out a debounce in real time (code-structure FR-051, SC-004).
/// </remarks>
public abstract class WatchedFolderStoreContract<TSnapshot> : IDisposable
    where TSnapshot : class
{
    /// <summary>The settle delay the store is made with, on <see cref="Clock"/>.</summary>
    /// <remarks>Properties rather than fields: a static field of a generic type is one per closed type.</remarks>
    private static TimeSpan SettleDelay => TimeSpan.FromMilliseconds(150);

    /// <summary>How long the file system is given to report a change.</summary>
    private static TimeSpan WaitLimit => TimeSpan.FromSeconds(15);

    /// <summary>How long a test listens, in real time, for something that must not be heard.</summary>
    protected static TimeSpan Silence => TimeSpan.FromMilliseconds(400);

    /// <summary>The clock the store's settle delay runs on.</summary>
    private FakeTimeProvider Clock { get; } = new();

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.WatchedFolderStoreContract");

    /// <summary>The store under test, made on first use and disposed with the test.</summary>
    protected WatchedFolderStore<TSnapshot> Store => field ??= CreateStore(SettleDelay, Clock);

    /// <summary>The folder the test works on, filled on first use.</summary>
    protected string Root => field ??= Fill("root");

    /// <summary>What the store is asked for: <see cref="Root"/>, or what names the snapshot inside it.</summary>
    protected string Subject => SubjectOf(Root);

    /// <summary>
    /// What names the snapshot read from the folder <paramref name="root"/>: the folder itself, unless
    /// the store's subject is a file in it, such as a solution.
    /// </summary>
    protected virtual string SubjectOf(string root) => root;

    /// <summary>A new store, with nothing loaded, that settles after <paramref name="settleDelay"/> on <paramref name="time"/>.</summary>
    protected abstract WatchedFolderStore<TSnapshot> CreateStore(TimeSpan settleDelay, TimeProvider time);

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
        var first = Store.GetOrLoad(Subject);
        var second = Store.GetOrLoad(Subject);

        // Assert: the same instance, not merely an equal one - one snapshot per folder is what
        // stops two connections seeing different things.
        Assert.Same(first, second);
    }

    [Fact]
    public void Get_DoesNotLoad()
    {
        // Act and assert, step by step.
        Assert.Null(Store.Get(Subject));
        Store.GetOrLoad(Subject);
        Assert.NotNull(Store.Get(Subject));
    }

    [Fact]
    public async Task AChange_IsAnnouncedWithTheRereadSnapshot_AndTheStoreHandsThatOneOut()
    {
        // Arrange.
        Store.GetOrLoad(Subject);
        var changes = Changes();

        // Act.
        await ChangeAsync(Root, TestContext.Current.CancellationToken);

        // Assert: re-read before announcing, so a listener that asks the store during the event
        // is not handed the snapshot the event exists to replace.
        var snapshot = await WaitForTheChange(changes);
        Assert.True(ShowsTheChange(snapshot));
        Assert.Same(snapshot, Store.Get(Subject));
    }

    [Fact]
    public async Task ABurstOfChanges_CostsFarFewerRereadsThanChanges()
    {
        // Arrange.
        const int changeCount = 6;
        Store.GetOrLoad(Subject);
        var changes = Changes();

        // Act: written back to back, as a multi-file save arrives.
        for (var i = 0; i < changeCount; i++)
        {
            await TouchAsync(Root, i, TestContext.Current.CancellationToken);
        }

        // Assert: the clock moves a settle delay only every settle delay of real time, as it would
        // run by itself, so events the file system is slow to report still fall into one window.
        // Exactly one re-read would test the file system's timing rather than the store.
        await WaitFor(changes, 1, SettleDelay);
        Assert.False(
            await Eventually.TrueAsync(() => Settled(() => Count(changes) >= changeCount), TestContext.Current.CancellationToken, Silence, SettleDelay),
            "Every change of the burst cost a re-read of its own.");
    }

    [Fact]
    public void ReleasingOneOfTwoClaims_KeepsTheFolderWatched()
    {
        // Arrange: a second connection closing must not tear the watcher out from under the
        // first, whose diagram would then go quietly stale.
        Store.Acquire(Subject);
        Store.Acquire(Subject);

        // Act.
        Store.Release(Subject);

        // Assert.
        Assert.NotNull(Store.Get(Subject));
    }

    [Fact]
    public void ReleasingTheLastClaim_DropsTheFolder()
    {
        // Arrange.
        Store.Acquire(Subject);

        // Act.
        Store.Release(Subject);

        // Assert.
        Assert.Null(Store.Get(Subject));
    }

    [Fact]
    public async Task AReleasedFolder_IsReadAfreshOnTheNextAsk()
    {
        // Arrange.
        var before = Store.Acquire(Subject);
        Store.Release(Subject);

        // Act: changed while nobody watches.
        await ChangeAsync(Root, TestContext.Current.CancellationToken);
        var after = Store.GetOrLoad(Subject);

        // Assert.
        Assert.NotSame(before, after);
        Assert.True(ShowsTheChange(after));
    }

    [Fact]
    public void ReleasingAFolderNobodyHeld_IsNotAnError()
    {
        // Act and assert.
        Store.Release(Subject);
        Store.Release(Path.Combine(Root, "never-loaded"));
    }

    [Fact]
    public async Task AWatch_HearsItsFolder_AndNothingOnceItIsLetGo()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Subject);
        var heard = new List<TSnapshot>();
        var watch = Store.Watch(Subject, snapshot =>
        {
            lock (heard)
            {
                heard.Add(snapshot);
            }
        });
        var changes = Changes();

        // Act: heard while watched, then let go - twice, which is harmless - and changed again.
        await ChangeAsync(Root, cancellationToken);
        await WaitForTheChange(changes);
        watch.Dispose();
        watch.Dispose();
        int heardBefore;
        lock (heard)
        {
            heardBefore = heard.Count;
        }
        int announcedBefore;
        lock (changes)
        {
            announcedBefore = changes.Count;
        }
        await TouchAsync(Root, 0, cancellationToken);
        await WaitFor(changes, announcedBefore + 1);

        // Assert: the change was heard, and nothing after the watch was let go.
        lock (heard)
        {
            Assert.Contains(heard, ShowsTheChange);
            Assert.Equal(heardBefore, heard.Count);
        }
    }

    [Fact]
    public async Task AWatch_DoesNotHearAnotherFolder()
    {
        // Arrange.
        var other = Fill("other");
        Store.GetOrLoad(Subject);
        Store.GetOrLoad(SubjectOf(other));
        var heard = new int[1];
        using var watch = Store.Watch(Subject, _ => Interlocked.Increment(ref heard[0]));
        var changes = Changes();

        // Act.
        await ChangeAsync(other, TestContext.Current.CancellationToken);
        await WaitFor(changes, 1);

        // Assert.
        Assert.Equal(0, Volatile.Read(ref heard[0]));
    }

    [Fact]
    public async Task AnOverflow_IsReadAgain_AsThoughAFileHadChanged()
    {
        // Arrange: the one signal a watcher gives when it dropped events is Error, and which file
        // changed is then unknowable, so the store must read again (backend-centralization R2.9).
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Subject);
        var changes = Changes();

        // Act: lose the change - nothing is watching while it happens, so no event can carry it -
        // then overflow, as the watcher itself would.
        var watchers = Store.WatchersOf(Subject);
        Assert.NotEmpty(watchers);
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
        }
        await ChangeAsync(Root, cancellationToken);
        watchers[0].RaiseOverflow();

        // Assert.
        var snapshot = await WaitFor(changes, 1);
        Assert.True(ShowsTheChange(snapshot), "An overflow left the snapshot looking fresh.");
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

    private static int Count(List<TSnapshot> changes)
    {
        lock (changes)
        {
            return changes.Count;
        }
    }

    /// <summary>
    /// <paramref name="probe"/>, asked once the store's clock has moved a settle delay: whatever the
    /// file system reported by now has been read and announced.
    /// </summary>
    private T Settled<T>(Func<T> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        Clock.Advance(SettleDelay);
        return probe();
    }

    /// <summary>Whether <paramref name="condition"/> came to hold of what the store announced, moving its clock while it waits.</summary>
    protected async Task<bool> SettlesAsync(Func<bool> condition, CancellationToken cancellationToken) =>
        await Eventually.TrueAsync(() => Settled(condition), cancellationToken, WaitLimit);

    /// <summary>Whether <paramref name="condition"/> held of anything the store announced while the test listened for <see cref="Silence"/>.</summary>
    protected async Task<bool> SettlesWithinTheSilenceAsync(Func<bool> condition, CancellationToken cancellationToken) =>
        await Eventually.TrueAsync(() => Settled(condition), cancellationToken, Silence);

    /// <summary>
    /// The first announced snapshot that shows the change. Not merely the first announced: a write
    /// lands as a truncation and then its content, and a file system that reports the two in
    /// different settle windows announces the empty file first, correctly.
    /// </summary>
    private async Task<TSnapshot> WaitForTheChange(List<TSnapshot> changes)
    {
        var snapshot = await Eventually.ValueAsync(
            () => Settled(() =>
            {
                lock (changes)
                {
                    return changes.FirstOrDefault(ShowsTheChange);
                }
            }),
            TestContext.Current.CancellationToken,
            WaitLimit);
        Assert.True(snapshot is not null, $"The change was not announced within {WaitLimit}.");
        return snapshot;
    }

    private async Task<TSnapshot> WaitFor(List<TSnapshot> changes, int count, TimeSpan? interval = null)
    {
        var snapshot = await Eventually.ValueAsync(
            () => Settled(() =>
            {
                lock (changes)
                {
                    return changes.Count >= count ? changes[^1] : null;
                }
            }),
            TestContext.Current.CancellationToken,
            WaitLimit,
            interval);
        Assert.True(snapshot is not null, $"The folder was not re-read within {WaitLimit}.");
        return snapshot;
    }
}
