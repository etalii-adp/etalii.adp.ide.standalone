using EtAlii.Adp.Specification.Fbl.Plugins;
using Microsoft.Extensions.Time.Testing;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The store (Requirement 1.4): one reading per folder, kept true as the folder changes through the library's
/// folder body, and kept open while a session claims it (helm-chart-disl-fbl, task 9). What the shared
/// watched-folder contract asked of the store is asked here of <see cref="HelmChartStore"/>, with the module's own
/// cases after it.
/// </summary>
/// <remarks>
/// Against a fresh chart per test rather than a fixture, because these tests deliberately change files.
/// </remarks>
public sealed class HelmChartStoreTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(HelmDefinition.Binding.Body.Settle);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    /// <summary>How long a test listens, in real time, for something that must not be heard.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.HelmChartStore");
    private readonly FakeTimeProvider _clock = new();
    private readonly HelmChartStore _store;
    private readonly string _root;

    public HelmChartStoreTests()
    {
        _store = new HelmChartStore(new HelmChartReader(), _clock);
        _root = Fill("root");
    }

    public void Dispose()
    {
        _store.Dispose();
        _folder.Dispose();
    }

    // ---- one reading per folder --------------------------------------------------------------

    [Fact]
    public void GetOrLoad_ReadsOnce_AndHandsTheSameFolderBack()
    {
        // Act.
        var first = _store.GetOrLoad(_root);
        var second = _store.GetOrLoad(_root);

        // Assert: the same instance and the same reading, not merely equal ones - one reading per folder is
        // what stops two connections seeing different things.
        Assert.Same(first, second);
        Assert.Same(first.Current, second.Current);
    }

    [Fact]
    public async Task AChange_IsAnnouncedWithTheRereadModel_AndTheStoreHandsThatOneOut()
    {
        // Arrange.
        Assert.False(ShowsTheChange(_store.GetOrLoad(_root).Current));
        var heard = Heard(_root);

        // Act.
        await ChangeAsync(_root, TestContext.Current.CancellationToken);

        // Assert: read again before announcing, so a listener that asks during the event is not handed the
        // reading the event exists to replace.
        var reading = await WaitFor(heard, ShowsTheChange);
        Assert.Same(reading, _store.GetOrLoad(_root).Current);
    }

    [Fact]
    public async Task ABurstOfChanges_CostsFarFewerReadsThanChanges()
    {
        // Arrange: a helm dependency build rewriting charts/ wholesale is the real-world shape of a burst.
        const int changeCount = 6;
        var reads = Reads(_store.GetOrLoad(_root));

        // Act: written back to back, as a multi-file save arrives. Any stall longer than the settle delay
        // lets the timer fire mid-burst, so exactly one read would be a clock test.
        for (var i = 0; i < changeCount; i++)
        {
            await TouchAsync(_root, i, TestContext.Current.CancellationToken);
        }

        // Assert: the clock moves a settle delay only every settle delay of real time, as it would run by
        // itself, so events the file system is slow to report still fall into one window.
        await WaitFor(reads, 1, SettleDelay);
        Assert.False(
            await Eventually.TrueAsync(() => Settled(() => Count(reads) >= changeCount), TestContext.Current.CancellationToken, Silence, SettleDelay),
            "Every change of the burst cost a read of its own.");
    }

    // ---- claims ------------------------------------------------------------------------------

    [Fact]
    public void ReleasingOneOfTwoClaims_KeepsTheFolderOpen()
    {
        // Arrange: a second connection closing must not close the folder under the first, whose diagram
        // would then go quietly stale.
        var held = _store.Acquire(_root);
        _store.Acquire(_root);

        // Act.
        _store.Release(_root);

        // Assert.
        Assert.Same(held, _store.GetOrLoad(_root));
    }

    [Fact]
    public void ReleasingTheLastClaim_ClosesTheFolder()
    {
        // Arrange.
        var held = _store.Acquire(_root);

        // Act.
        _store.Release(_root);

        // Assert.
        Assert.NotSame(held, _store.GetOrLoad(_root));
    }

    [Fact]
    public async Task AReleasedFolder_IsReadAfreshOnTheNextAsk()
    {
        // Arrange.
        var before = _store.Acquire(_root);
        _store.Release(_root);

        // Act: changed while nobody watches.
        await ChangeAsync(_root, TestContext.Current.CancellationToken);
        var after = _store.GetOrLoad(_root);

        // Assert.
        Assert.NotSame(before, after);
        Assert.True(ShowsTheChange(after.Current));
    }

    [Fact]
    public void ReleasingAFolderNobodyHeld_IsNotAnError()
    {
        // Act and assert.
        _store.Release(_root);
        _store.Release(IoPath.Combine(_root, "never-loaded"));
    }

    [Fact]
    public void AFolderOpenedWithItsRegistration_IsNotTheFolderOpenedWithout()
    {
        // Arrange: a folder is keyed by the registration that holds its positions, so the folder body
        // watches the file each session writes to.
        var registration = IoPath.Combine(_root, "helm-chart.adp");
        File.WriteAllText(registration, "helm/chart\n");

        // Act.
        var with = _store.GetOrLoad(_root, registration);
        var without = _store.GetOrLoad(_root);

        // Assert.
        Assert.NotSame(with, without);
        Assert.Same(with, _store.GetOrLoad(_root, registration));
        Assert.Equal(IoPath.GetFullPath(registration), with.Registration);
    }

    // ---- watches -----------------------------------------------------------------------------

    [Fact]
    public async Task AWatch_HearsItsFolder_AndNothingOnceItIsLetGo()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var opened = _store.GetOrLoad(_root);
        var heard = new List<HelmReading>();
        var watch = _store.Watch(_root, null, folder =>
        {
            lock (heard)
            {
                heard.Add(folder.Current);
            }
        });
        var reads = Reads(opened);

        // Act: heard while watched, then let go - twice, which is harmless - and changed again.
        await ChangeAsync(_root, cancellationToken);
        await WaitFor(heard, ShowsTheChange);
        watch.Dispose();
        watch.Dispose();
        int heardBefore;
        lock (heard)
        {
            heardBefore = heard.Count;
        }
        int readBefore;
        lock (reads)
        {
            readBefore = reads.Count;
        }
        await TouchAsync(_root, 0, cancellationToken);
        await WaitFor(reads, readBefore + 1);

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
        _store.GetOrLoad(_root);
        var reads = Reads(_store.GetOrLoad(other));
        var heard = new int[1];
        using var watch = _store.Watch(_root, null, _ => Interlocked.Increment(ref heard[0]));

        // Act.
        await ChangeAsync(other, TestContext.Current.CancellationToken);
        await WaitFor(reads, 1);

        // Assert.
        Assert.Equal(0, Volatile.Read(ref heard[0]));
    }

    [Fact]
    public async Task AnOverflow_IsReadAgain_AsThoughAFileHadChanged()
    {
        // Arrange: the one signal a watcher gives when it dropped events is Error, and which file changed
        // is then unknowable, so the folder must be read again (backend-centralization R2.9).
        var cancellationToken = TestContext.Current.CancellationToken;
        var opened = _store.GetOrLoad(_root);
        var heard = Heard(_root);

        // Act: lose the change - nothing is watching while it happens, so no event can carry it - then
        // overflow, as the watcher itself would.
        var watcher = opened.FolderWatcher;
        Assert.NotNull(watcher);
        watcher.EnableRaisingEvents = false;
        await ChangeAsync(_root, cancellationToken);
        watcher.RaiseOverflow();

        // Assert.
        var reading = await WaitFor(heard, _ => true);
        Assert.True(ShowsTheChange(reading), "An overflow left the reading looking fresh.");
    }

    // ---- the module's own cases ----------------------------------------------------------------

    [Fact]
    public void ThereIsNoSavePath()
    {
        // Assert.
        // Structural, per Requirement 7.1: the store's public surface has nothing that takes
        // content to write - reflection proves the absence rather than a comment claiming it.
        var writers = new[] { typeof(IHelmChartStore), typeof(HelmChartStore), typeof(HelmBody) }
            .SelectMany(type => type.GetMethods())
            .Where(method => method.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                             || method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase)
                             || method.Name.Contains("Update", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(writers);
    }

    [Fact]
    public async Task AnEmptyCrdsFolder_IsPickedUp()
    {
        // Arrange: a directory no file rule selects (gap 3), still read again because a file rule's glob could add a file to it.
        var heard = Heard(_root);

        // Act.
        Directory.CreateDirectory(IoPath.Combine(_root, "crds"));

        // Assert.
        await WaitFor(heard, reading => reading.Disl.Diagram.Nodes.Any(node => node.Id == "crds"));
    }

    [Fact]
    public async Task AWriteToTheRegistration_IsAnnouncedWithoutReadingTheFolder()
    {
        // Arrange: the registration is no file rule's file, yet a position written to it must reach every open
        // reading of the folder (Requirement 4.5, finding B12).
        var registration = IoPath.Combine(_root, "helm-chart.adp");
        await File.WriteAllTextAsync(registration, "helm/chart\n", TestContext.Current.CancellationToken);
        var opened = _store.GetOrLoad(_root, registration);
        var changes = new List<FolderBodyChange>();
        opened.Changed += (_, change) =>
        {
            lock (changes)
            {
                changes.Add(change);
            }
        };

        // Act.
        await File.WriteAllTextAsync(registration, "helm/chart\nlayout:\n  chart: { x: 1.000, y: 2.000 }\n", TestContext.Current.CancellationToken);

        // Assert.
        var change = await Eventually.ValueAsync(
            () => Settled(() =>
            {
                lock (changes)
                {
                    return changes.FirstOrDefault();
                }
            }),
            TestContext.Current.CancellationToken,
            WaitLimit);
        Assert.NotNull(change);
        Assert.True(change.Registration);
        Assert.False(change.FolderRead);
    }

    // ---- plumbing ------------------------------------------------------------------------------

    /// <summary>A fresh chart named <paramref name="name"/>.</summary>
    private string Fill(string name)
    {
        var root = _folder.Combine(name);
        Directory.CreateDirectory(root);
        File.WriteAllText(IoPath.Combine(root, "Chart.yaml"), "apiVersion: v2\nname: watched\nversion: 1.0.0\n");
        return root;
    }

    /// <summary>The chart gains its default values file.</summary>
    private static Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(IoPath.Combine(root, "values.yaml"), "replicaCount: 1\n", cancellationToken);

    private static bool ShowsTheChange(HelmReading reading) =>
        reading.Disl.Diagram.Nodes.Any(node => node.Id == "values:values.yaml");

    private static Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(IoPath.Combine(root, $"values-{index}.yaml"), $"layer: {index}\n", cancellationToken);

    /// <summary>Every reading the store announces for <paramref name="root"/> from now on, opening it first.</summary>
    private List<HelmReading> Heard(string root)
    {
        _store.GetOrLoad(root);
        var heard = new List<HelmReading>();
        // Disposed with the store.
        _ = _store.Watch(root, null, folder =>
        {
            lock (heard)
            {
                heard.Add(folder.Current);
            }
        });
        return heard;
    }

    /// <summary>Every change of <paramref name="opened"/> from now on that read the folder again.</summary>
    private static List<FolderBodyChange> Reads(HelmBody opened)
    {
        var reads = new List<FolderBodyChange>();
        opened.Changed += (_, change) =>
        {
            if (!change.FolderRead) return;
            lock (reads)
            {
                reads.Add(change);
            }
        };
        return reads;
    }

    /// <summary>
    /// The first announced reading that <paramref name="shows"/> holds of. Not merely the first announced: a
    /// write lands as a truncation and then its content, and a runner that stalls between the two for longer
    /// than the settle delay announces the empty file first, correctly.
    /// </summary>
    private async Task<HelmReading> WaitFor(List<HelmReading> heard, Func<HelmReading, bool> shows)
    {
        var reading = await Eventually.ValueAsync(
            () => Settled(() =>
            {
                lock (heard)
                {
                    return heard.FirstOrDefault(shows);
                }
            }),
            TestContext.Current.CancellationToken,
            WaitLimit);
        Assert.True(reading is not null, $"The change was not announced within {WaitLimit}.");
        return reading;
    }

    private async Task WaitFor(List<FolderBodyChange> reads, int count, TimeSpan? interval = null) =>
        Assert.True(
            await Eventually.TrueAsync(() => Settled(() => Count(reads) >= count), TestContext.Current.CancellationToken, WaitLimit, interval),
            $"The folder was not read again within {WaitLimit}.");

    private static int Count(List<FolderBodyChange> reads)
    {
        lock (reads)
        {
            return reads.Count;
        }
    }

    /// <summary><paramref name="probe"/>, asked once the store's clock has moved a settle delay.</summary>
    private T Settled<T>(Func<T> probe)
    {
        _clock.Advance(SettleDelay);
        return probe();
    }
}
